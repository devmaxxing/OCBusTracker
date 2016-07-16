using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http;
using Newtonsoft.Json.Linq;
using Windows.UI.Popups;
using Windows.Networking.Connectivity;
using SQLite.Net.Attributes;
using Windows.UI.Xaml;
using Windows.UI.Core;
//using System.Diagnostics;
// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace OC_Bus_Tracker
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public HttpClient httpClient = new HttpClient();
        public ObservableCollection<RouteInfo> routesList = new ObservableCollection<RouteInfo>();
        public ObservableCollection<RouteInfo> savedRoutes = new ObservableCollection<RouteInfo>();
        public ObservableCollection<string> stopNames = new ObservableCollection<string>();
        public ObservableCollection<string> filtered = new ObservableCollection<string>();
        public SQLite.Net.SQLiteConnection conn;
        public string httpQueryParams = "/?appID=" + OCTranspoInfo.APP_ID + "&apiKey=" + OCTranspoInfo.API_KEY + "&format=json";
        public string path;

        public MainPage()
        {
            this.InitializeComponent();
            loadStops();

            SearchBox.ItemsSource = filtered;
            resultsList.DataContext = routesList;
            pinnedList.DataContext = savedRoutes;

            path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,
            "db.sqlite");
            conn = new SQLite.Net.SQLiteConnection(new
               SQLite.Net.Platform.WinRT.SQLitePlatformWinRT(), path);            
            conn.CreateTable<StopRoute>();
        }

        private async Task<string> loadStops()
        {
            var uri = new System.Uri("ms-appx:///stops.json");
            var stopsFile = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);
            var stopsText = await Windows.Storage.FileIO.ReadTextAsync(stopsFile);
            JArray stops = (JArray)JArray.Parse(stopsText);
            foreach(var stop in stops)
            {
                stopNames.Add(stop["stop_code"] + " " + stop["stop_name"]);
            }
            return "success";
        }

        private async void loadPinned()
        {
            var query = conn.Table<StopRoute>();
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    savedRoutes.Clear();
                }
                );
            SearchRingPinned.IsActive = true;
            SearchRingPinned.Visibility = Visibility.Visible;
            foreach (var stopRoute in query)
            {
                if (IsInternet())
                {

                    var tripTimes = new string[3] { "No upcoming trips", "", "" };

                    HttpRequestMessage stopRouteRequest = new HttpRequestMessage();
                    var queryString = httpQueryParams + "&stopNo=" + stopRoute.stopId + "&routeNo=" + stopRoute.routeNum;
                    stopRouteRequest.RequestUri = new Uri(OCTranspoInfo.ROUTE_URL + queryString);

                    var response = await getJSON(stopRouteRequest);
                    JObject routeInfo = (JObject)JObject.Parse(response)["GetNextTripsForStopResult"];
                    
                    if ((string)routeInfo["Error"] == "")
                    {
                        JObject route;
                        try
                        {
                            route = (JObject)routeInfo["Route"]["RouteDirection"];
                        }
                        catch (Exception e)
                        {
                            route = (JObject)routeInfo["Route"]["RouteDirection"][stopRoute.directionId];
                        }

                        JArray trips = new JArray();
                        if (route["Trips"].HasValues)
                        {
                            try
                            {
                                trips = (JArray)route["Trips"];
                            }
                            catch (Exception e)
                            {
                                try
                                {
                                    trips = (JArray)route["Trips"]["Trip"];
                                }
                                catch (Exception e2)
                                {
                                    trips.Add((JObject)route["Trips"]);
                                }
                            }
                        }

                        var numTrips = trips.Count();
                        for (var j = 0; j < numTrips; j++)
                        {
                            tripTimes[j] = (string)trips[j]["AdjustedScheduleTime"] + " min";
                        }
                    }
                   
                    RouteInfo r = new RouteInfo(stopRoute, tripTimes[0], tripTimes[1], tripTimes[2]);

                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>{
                        savedRoutes.Add(r);
                    });
                }else
                {
                    MessageDialog msgDialog = new MessageDialog("No internet connection.", "Error");
                    await msgDialog.ShowAsync();
                }
                
            }
            SearchRingPinned.IsActive = false;
            SearchRingPinned.Visibility = Visibility.Collapsed;
        }

        private async void SearchStop(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (IsInternet())
            {
                SearchRing.IsActive = true;
                SearchRing.Visibility = Visibility.Visible;

                HttpRequestMessage stopRequest = new HttpRequestMessage();
                var queryString = httpQueryParams + "&stopNo=" + sender.Text.Split(' ')[0];
                stopRequest.RequestUri = new Uri(OCTranspoInfo.STOP_URL + queryString);

                var response = await getJSON(stopRequest);
                JObject routeInfo = (JObject)JObject.Parse(response)["GetRouteSummaryForStopResult"];
                SearchRing.IsActive = false;
                SearchRing.Visibility = Visibility.Collapsed;
                if ((string)routeInfo["Error"] == "")
                {
                    addRouteInfo(routesList, routeInfo);
                }
                else
                {
                    MessageDialog msgDialog = new MessageDialog("Sorry, we were unable to get the requested stop info.", "Error");
                    await msgDialog.ShowAsync();
                }
            }
            else
            {
                MessageDialog msgDialog = new MessageDialog("No internet connection.", "Error");
                await msgDialog.ShowAsync();
            }
                        
        }

        private async void addRouteInfo (ObservableCollection<RouteInfo> collection, JObject routeInfo)
        {
            JArray routes = new JArray();
            try
            {
                routes = (JArray)routeInfo["Routes"]["Route"];
            }catch(ArgumentException e1)
            {
                routes = (JArray)routeInfo["Routes"];
            }catch(Exception e)
            {
                routes.Add((JObject)routeInfo["Routes"]["Route"]);
            }

            collection.Clear();
            var stopName = (string)routeInfo["StopDescription"];
            var stopId = (string)routeInfo["StopNo"];
            var numRoutes = routes.Count();

            if(numRoutes == 0)
            {
                MessageDialog msgDialog = new MessageDialog("There are no upcoming trips at this stop.", "Message");
                await msgDialog.ShowAsync();
            }

            for (var i = 0; i < numRoutes; i++)
            {
                var route = routes[i];
                var tripTimes = new string[3] { "", "", "" };
                
                var routeName = (string)route["RouteNo"] + " " + (string)route["RouteHeading"];
                var routeNum = (string) route["RouteNo"];
                var directionId = 0;
                try
                {
                    directionId = (int)route["DirectionID"];
                }
                catch (Exception e) { }

                JArray trips;
                try
                {
                    trips = (JArray)route["Trips"];
                }
                catch (Exception e)
                {
                    try
                    {
                        trips = (JArray)route["Trips"]["Trip"];
                    }catch(Exception e2)
                    {
                        trips = new JArray();
                        trips.Add((JObject)route["Trips"]);
                    }                    
                }

                if (trips != null)
                {
                    var numTrips = trips.Count();
                    if (numTrips > 0)
                    {
                        for (var j = 0; j < numTrips; j++)
                        {
                            tripTimes[j] = (string)trips[j]["AdjustedScheduleTime"] + " min";
                        }
                        RouteInfo r = new RouteInfo(stopName, stopId, routeName, routeNum, directionId, tripTimes[0], tripTimes[1], tripTimes[2]);
                        collection.Add(r);
                    }
                }                
            }
        }

        private bool IsInternet()
        {
            ConnectionProfile connections = NetworkInformation.GetInternetConnectionProfile();
            bool internet = connections != null && connections.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            return internet;
        }

        private async Task<String> getJSON(HttpRequestMessage message)
        {
            HttpResponseMessage response = await httpClient.SendRequestAsync(message);
            return response.Content.ToString();
        }

        public class StopRoute
        {
            [PrimaryKey, AutoIncrement]
            public int id { get; set; }
            public string stopId { get; set; }
            public string stopName { get; set; }
            public string routeNum { get; set; }
            public string routeName { get; set; }
            public int directionId { get; set; }
        }

        public class RouteInfo
        {
            public RouteInfo(StopRoute sr, string t1, string t2, string t3)
            {
                stopRoute = sr;
                RouteTime1 = t1;
                RouteTime2 = t2;
                RouteTime3 = t3;
            }

            public RouteInfo(string stopName, string stopId, string routeName, string routeNum, int directionId, string t1, string t2, string t3) : this(new StopRoute(), t1,t2,t3)
            {
                stopRoute.stopName = stopName;
                stopRoute.stopId = stopId;
                stopRoute.routeName = routeName;
                stopRoute.routeNum = routeNum;
                stopRoute.directionId = directionId;              
            }

            public StopRoute stopRoute { get; set; }
            public string RouteTime1 { get; set; }
            public string RouteTime2 { get; set; }
            public string RouteTime3 { get; set; }
        }

        private void PinRoutes(object sender, RoutedEventArgs e)
        {
            var selectedItems = resultsList.SelectedItems.OfType<RouteInfo>();
            foreach(var item in selectedItems)
            {
                StopRoute sr = item.stopRoute;
                if (savedRoutes.Where(route =>
                route.stopRoute.stopId == sr.stopId && route.stopRoute.routeNum == sr.routeNum &&
                route.stopRoute.directionId == sr.directionId).Count() == 0)
                {
                    var s = conn.Insert(sr);
                    savedRoutes.Add(item);
                }
            }
            DisableSelection();
        }

        private void PivotChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Contains(PinnedPivot))
            {
                PinButton.Icon = new SymbolIcon(Symbol.UnPin);
                PinButton.Label = "Unpin Selected";
                PinButton.Click -= PinRoutes;
                PinButton.Click += UnpinSelected;
                loadPinned();
            }else
            {
                PinButton.Icon = new SymbolIcon(Symbol.Pin);
                PinButton.Label = "Pin Selected";
                PinButton.Click -= UnpinSelected;
                PinButton.Click += PinRoutes;
            }
            DisableSelection();
        }

        private void UnpinSelected(object sender, RoutedEventArgs e)
        {
            var selectedItems = pinnedList.SelectedItems.OfType<RouteInfo>();
            foreach (var item in selectedItems)
            {
                
                var query = conn.Table<StopRoute>().Where(
                    s => s.directionId == item.stopRoute.directionId && s.routeNum == item.stopRoute.routeNum && s.stopId == item.stopRoute.stopId);
                var sr = query.ElementAt(0);
                var d = conn.Delete<StopRoute>(sr.id);
                savedRoutes.Remove(item);
            }
            DisableSelection();
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            filtered.Clear();
            if(sender.Text!="")
                filtered = new ObservableCollection<string>(stopNames.Where(stopName => stopName.Contains(sender.Text.ToUpper())).Take(8));
            sender.ItemsSource = filtered;
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            sender.Text = args.SelectedItem.ToString();
        }

        private void Select(object sender, RoutedEventArgs e)
        {
            ListView currentList;
            if (rootPivot.SelectedItem == PinnedPivot)
            {
                currentList = pinnedList;
            }else
            {
                currentList = resultsList;
            }
            if(currentList.SelectionMode == ListViewSelectionMode.None)
            {
                currentList.SelectionMode = ListViewSelectionMode.Multiple;
                PinButton.IsEnabled = true;
                menuBar.ClosedDisplayMode = AppBarClosedDisplayMode.Compact;
            }else
            {
                currentList.SelectionMode = ListViewSelectionMode.None;
                PinButton.IsEnabled = false;
                menuBar.ClosedDisplayMode = AppBarClosedDisplayMode.Minimal;
            }
        }

        private void DisableSelection(object sender, object e)
        {
            DisableSelection();
        }

        private void DisableSelection()
        {
            ListView currentList;
            if (rootPivot.SelectedItem == PinnedPivot)
            {
                currentList = pinnedList;
            }
            else
            {
                currentList = resultsList;
            }
            currentList.SelectionMode = ListViewSelectionMode.None;
            PinButton.IsEnabled = false;
            menuBar.ClosedDisplayMode = AppBarClosedDisplayMode.Minimal;
        }

        private void Reload(object sender, RoutedEventArgs e)
        {
            if(rootPivot.SelectedItem == PinnedPivot)
            {
                loadPinned();
            }else
            {
                SearchStop(SearchBox, null);
            }
        }
    }
}
