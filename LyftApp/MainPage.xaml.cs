using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Geolocation;
using Windows.UI.Xaml.Controls.Maps;
using System.Threading.Tasks;
using Shyft.Models;
using Windows.Services.Maps;
using System.Diagnostics;
using Google.Maps.Places;
using Google.Maps.Places.Details;
using Shyft;
using Windows.UI;
using Humanizer;
using Windows.UI.Xaml.Media.Imaging;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace LyftApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public enum RideStates
        {
            Pickup,
            Dropoff,
            RideStaging,
            Ride,
            Complete
        }

        private RideStates rideState;
        private Geolocator geolocator;

        private List<RideType> availableRideTypes;
        private List<LyftConstants.RideType> preferredRideTypes = new List<LyftConstants.RideType>()
        {
            LyftConstants.RideType.Lyft,
            LyftConstants.RideType.LyftLine,
            LyftConstants.RideType.LyftPlus,
            LyftConstants.RideType.LyftLux,
            LyftConstants.RideType.LyftLuxSuv,
            LyftConstants.RideType.LyftPremier
        };

        private MapIcon userLocation;
        private MapIcon startLocation;
        private MapIcon endLocation;

        private List<MapElement> driverList;
        private DateTime? addressLastChecked;
        private PlacesService placesService;
        private PlaceDetailsService placeDetailsService;
        private Dictionary<string, string> addressToId;
        private bool reverseGeocode;
        private BasicGeoposition lastCenter;
        private bool loaded;

        private BasicGeoposition? pickupLocation;
        private BasicGeoposition? dropoffLocation;
        private CostEstimate currentCostEstimate;

        private RideRequest rideRequest;
        private RideDetail rideDetails;
        private MapIcon driverLocation;

        public MainPage()
        {
            this.InitializeComponent();

            rideState = RideStates.Pickup;
            availableRideTypes = new List<RideType>();

            geolocator = new Geolocator();
            geolocator.AllowFallbackToConsentlessPositions();
            geolocator.ReportInterval = (uint)TimeSpan.FromSeconds(15).TotalSeconds;
            geolocator.PositionChanged += Geolocator_PositionChanged;

            driverList = new List<MapElement>();

            map.MapServiceToken = Secrets.MapToken;
            map.BusinessLandmarksVisible = true;
            //map.LandmarksVisible = true;
            //map.PedestrianFeaturesVisible = true;
            //map.TrafficFlowVisible = true;
            //map.TransitFeaturesVisible = true;

            placesService = new PlacesService();
            placeDetailsService = new PlaceDetailsService();
            addressToId = new Dictionary<string, string>();
            reverseGeocode = true;
            loaded = false;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            GeolocationAccessStatus accessStatus = await Geolocator.RequestAccessAsync();
            //if (accessStatus == GeolocationAccessStatus.Unspecified)
            //{
            //    // don't actually do this
            //    throw new Exception();
            //}

            Geoposition position = await geolocator.GetGeopositionAsync();
            map.Center = position.Coordinate.Point;
            map.ZoomLevel = 15;
            List<MapElement> layer = new List<MapElement>();
            userLocation = new MapIcon();
            userLocation.Location = position.Coordinate.Point;
            userLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            userLocation.Title = "Your location";
            map.MapElements.Add(userLocation);

            await SetupPin(RideStates.Pickup, position);

            UpdateFromMapCenterTimer();
            await GetAvailableTypes(map.Center.Position);
            var preferredRideType = GetPreferredType(availableRideTypes);
            if (preferredRideType.HasValue)
            {
                SelectPreferredType(preferredRideType.Value);
                await DisplayDrivers(map.Center.Position, preferredRideType.Value);
                await GetEta(map.Center.Position, preferredRideType.Value);
                loaded = true;
            }
            else
            {
                button.IsEnabled = false;
                button.Content = "No rides available";
            }
        }

        private async void Geolocator_PositionChanged(Geolocator sender, PositionChangedEventArgs args)
        {
            if (userLocation != null)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    userLocation.Location = args.Position.Coordinate.Point;
                });
            }
        }

        private async Task UpdateFromMapCenterTimer()
        {
            while (true)
            {
                if (rideState == RideStates.Pickup ||
                    rideState == RideStates.Dropoff)
                {
                    if (!map.Center.Position.EqualsOther(lastCenter))
                    {
                        await ReverseGeocode(new Geopoint(map.Center.Position));
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private async Task GetAvailableTypes(BasicGeoposition geoposition)
        {
            availableRideTypes = await AppConstants.ShyftClient.RetrieveRideTypes(geoposition.Latitude, geoposition.Longitude);
            rideTypeComboBox.Items.Clear();
            foreach (var rideType in availableRideTypes)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Content = rideType.DisplayName;
                rideTypeComboBox.Items.Add(item);
            }
        }

        private async Task DisplayDrivers(BasicGeoposition geoposition, LyftConstants.RideType rideType)
        {
            var nearbyDrivers = await AppConstants.ShyftClient.RetrieveNearbyDrivers(geoposition.Latitude, geoposition.Longitude);
            ClearDriversList();
            foreach (var nearbyDriver in nearbyDrivers)
            {
                if (nearbyDriver.RideType == rideType)
                {
                    foreach (var driver in nearbyDriver.Drivers)
                    {
                        driverList.Add(CreatePin(driver.Locations.Last()));
                    }
                    break;
                }
            }

            foreach (var driver in driverList)
            {
                map.MapElements.Add(driver);
            }
        }

        private void ClearDriversList()
        {
            foreach (var driver in driverList)
            {
                map.MapElements.Remove(driver);
            }
            driverList.Clear();
        }

        private async Task GetEta(BasicGeoposition geoposition, LyftConstants.RideType rideType)
        {
            int? etaSeconds = null;
            int? etaSecondsMax = null;
            var etas = await AppConstants.ShyftClient.RetrieveDriverEta(geoposition.Latitude, geoposition.Longitude, null, null, rideType);
            foreach (var eta in etas)
            {
                if (eta.RideType == rideType && eta.IsValidEstimate && eta.EtaSeconds != null)
                {
                    etaSeconds = eta.EtaSeconds;
                    etaSecondsMax = eta.EtaSecondsMax;
                    break;
                }
            }

            if (!etaSeconds.HasValue)
            {
                etaTextBlock.Text = "No ETA";
            }
            else
            {
                if (etaSeconds.Value < 60)
                {
                    etaTextBlock.Text = $"{etaSeconds.Value} sec";
                }
                else
                {
                    etaTextBlock.Text = $"{Math.Round(TimeSpan.FromSeconds(etaSeconds.Value).TotalMinutes)} min";
                }

                if (etaSecondsMax.HasValue)
                {
                    if (etaSecondsMax.Value < 60)
                    {
                        etaTextBlock.Text += $" - {etaSecondsMax.Value} sec";
                    }
                    else
                    {
                        etaTextBlock.Text += $" - {Math.Round(TimeSpan.FromSeconds(etaSecondsMax.Value).TotalMinutes)} min";
                    }
                }
            }
        }

        private async Task GetCostEstimate(BasicGeoposition start, BasicGeoposition end, LyftConstants.RideType rideType)
        {
            var costEstimates = await AppConstants.ShyftClient.RetrieveRideEstimates(start.Latitude, start.Longitude,
                end.Latitude, end.Longitude,
                rideType);
            foreach (var costEstimate in costEstimates)
            {
                if (costEstimate.RideType == rideType)
                {
                    currentCostEstimate = costEstimate;
                    costTextBlock.Text = string.Empty;
                    if (!costEstimate.IsValidEstimate)
                    {
                        costTextBlock.Text = "Unknown cost!\n";
                    }
                    else
                    {
                        costTextBlock.Text = $"${costEstimate.EstimatedCostCentsMin / 100f}";
                        if (costEstimate.EstimatedCostCentsMin != costEstimate.EstimatedCostCentsMax)
                        {
                            costTextBlock.Text += $" - ${costEstimate.EstimatedCostCentsMax / 100f}";
                        }
                    }

                    if (costEstimate.PrimetimePercentage != "0%")
                    {
                        costTextBlock.Text += $"\nPrimetime: {costEstimate.PrimetimePercentage}";
                    }

                    TimeSpan timeEstimate = TimeSpan.FromSeconds(costEstimate.EstimatedDurationSeconds);
                    costTextBlock.Text += $"\n{Math.Round(timeEstimate.TotalMinutes)} min";
                    costTextBlock.Text += $"\n{costEstimate.EstimatedDistanceMiles} miles";
                    break;
                }
            }
        }

        private MapIcon CreatePin(LatLng latLng)
        {
            MapIcon icon = new MapIcon();
            SetPinLocation(icon, latLng);
            icon.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            return icon;
        }

        private void SetPinLocation(MapIcon icon, LatLng latLng)
        {
            icon.Location = new Geopoint(new BasicGeoposition()
            {
                Latitude = latLng.Lat,
                Longitude = latLng.Lng
            });
        }

        private async void map_CenterChanged(MapControl sender, object args)
        {
            if (rideState == RideStates.Pickup || rideState == RideStates.Dropoff)
            {
                if (startLocation != null || endLocation != null)
                {
                    if (rideState == RideStates.Pickup)
                    {
                        startLocation.Location = map.Center;
                    }
                    else if (rideState == RideStates.Dropoff)
                    {
                        endLocation.Location = map.Center;
                    }

                    if (addressLastChecked == null ||
                        DateTime.Now > addressLastChecked.Value + TimeSpan.FromSeconds(5))
                    {
                        addressLastChecked = DateTime.Now;
                        if (reverseGeocode)
                        {
                            await ReverseGeocode(new Geopoint(map.Center.Position));
                        }
                        else
                        {
                            reverseGeocode = true;
                        }
                    }
                }
            }
        }

        private async Task ReverseGeocode(Geopoint geopoint)
        {
            MapLocationFinderResult result = await MapLocationFinder.FindLocationsAtAsync(geopoint);
            if (result.Status == MapLocationFinderStatus.Success && result.Locations.Count > 0)
            {
                searchBox.Text = result.Locations[0].DisplayName;
                lastCenter = geopoint.Position;
            }
        }

        private async void searchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                if (searchBox.Text.Trim().Length >= 2)
                {
                    AutocompleteRequest autocompleteRequest = new AutocompleteRequest();
                    autocompleteRequest.Input = searchBox.Text.Trim();
                    autocompleteRequest.Location = new Google.Maps.LatLng(map.Center.Position.Latitude, map.Center.Position.Longitude);
                    AutocompleteResponse autocompleteResponse = await placesService.GetAutocompleteResponseAsync(autocompleteRequest);
                    if (autocompleteResponse.Status == Google.Maps.ServiceResponseStatus.Ok)
                    {
                        searchBox.Items.Clear();
                        addressToId.Clear();
                        foreach (var prediction in autocompleteResponse.Predictions)
                        {
                            searchBox.Items.Add(prediction.description);
                            addressToId.Add(prediction.description, prediction.PlaceId);
                        }
                    }
                }
            }
        }

        private void searchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            searchBox.Text = (string)args.SelectedItem;
        }

        private void searchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (addressToId.ContainsKey(searchBox.Text))
            {
                PlaceDetailsRequest placeDetailsRequest = new PlaceDetailsRequest();
                placeDetailsRequest.PlaceID = addressToId[searchBox.Text];
                PlaceDetailsResponse placeDetailsResponse = placeDetailsService.GetResponse(placeDetailsRequest);
                if (placeDetailsResponse.Status == Google.Maps.ServiceResponseStatus.Ok)
                {
                    reverseGeocode = false;
                    map.Center = new Geopoint(new BasicGeoposition()
                    {
                        Latitude = placeDetailsResponse.Result.Geometry.Location.Latitude,
                        Longitude = placeDetailsResponse.Result.Geometry.Location.Longitude
                    });
                }
            }
        }

        private async void rideTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loaded)
            {
                LyftConstants.RideType? selectedRideType = null;
                if (e.AddedItems.Count > 0)
                {
                    string selected = (string)((ComboBoxItem)e.AddedItems[0]).Content;
                    foreach (var availableRideType in availableRideTypes)
                    {
                        if (selected == availableRideType.DisplayName)
                        {
                            selectedRideType = availableRideType.Type;
                            break;
                        }
                    }

                    if (selectedRideType.HasValue)
                    {
                        if (rideState == RideStates.Pickup || rideState == RideStates.Dropoff)
                        {
                            await DisplayDrivers(map.Center.Position, selectedRideType.Value);
                            await GetEta(map.Center.Position, selectedRideType.Value);
                        }
                        else if (rideState == RideStates.RideStaging)
                        {
                            await GetCostEstimate(pickupLocation.Value, dropoffLocation.Value, selectedRideType.Value);
                        }
                    }
                }
            }
        }

        private LyftConstants.RideType? GetSelectedRideType()
        {
            string selected = (string)((ComboBoxItem)rideTypeComboBox.SelectedItem).Content;
            foreach (var availableRideType in availableRideTypes)
            {
                if (selected == availableRideType.DisplayName)
                {
                    return availableRideType.Type;
                }
            }
            return null;
        }

        private async void button_Click(object sender, RoutedEventArgs e)
        {
            if (rideState == RideStates.Pickup)
            {
                pickupSearchBox.Text = searchBox.Text;
                pickupSearchBox.Visibility = Visibility.Visible;
                await SetupPin(RideStates.Dropoff);
                pickupLocation = startLocation.Location.Position;
                button.Content = "Set destination";
                rideState = RideStates.Dropoff;
            }
            else if (rideState == RideStates.Dropoff)
            {
                dropoffSearchBox.Text = searchBox.Text;
                dropoffSearchBox.Visibility = Visibility.Visible;
                searchBox.Visibility = Visibility.Collapsed;
                dropoffLocation = endLocation.Location.Position;
                var selectedRideType = GetSelectedRideType();
                if (selectedRideType.HasValue)
                {
                    costTextBlock.Visibility = Visibility.Visible;
                    await GetCostEstimate(pickupLocation.Value, dropoffLocation.Value, selectedRideType.Value);
                    button.Content = $"Request {GetDisplayName(selectedRideType.Value)}";
                    rideState = RideStates.RideStaging;
                    await ShowRoute(pickupLocation.Value, dropoffLocation.Value);
                }
            }
            else if (rideState == RideStates.RideStaging)
            {
                var selectedRideType = GetSelectedRideType();
                if (selectedRideType.HasValue)
                {
                    rideRequest = await AppConstants.ShyftClient.RequestRide(pickupLocation.Value.Latitude, pickupLocation.Value.Longitude,
                        dropoffLocation.Value.Latitude, dropoffLocation.Value.Longitude,
                        selectedRideType.Value);
                    rideTypeComboBox.Visibility = Visibility.Collapsed;
                    etaTextBlock.Visibility = Visibility.Collapsed;
                    pickupSearchBox.Visibility = Visibility.Collapsed;
                    dropoffSearchBox.Visibility = Visibility.Collapsed;
                    button.Visibility = Visibility.Collapsed;
                    advanceButton.Visibility = Visibility.Visible;
                    ClearDriversList();
                    rideState = RideStates.Ride;
                    RefreshRideDetails();
                }
            }
        }

        private async Task SetupPin(RideStates state, Geoposition position = null)
        {
            if (position == null)
            {
                position = await geolocator.GetGeopositionAsync();
            }
            if (state == RideStates.Pickup)
            {
                if (startLocation != null && map.MapElements.Contains(startLocation))
                {
                    map.MapElements.Remove(startLocation);
                }
                startLocation = new MapIcon();
                startLocation.Location = position.Coordinate.Point;
                startLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
                startLocation.Title = "Pickup location";
                map.MapElements.Add(startLocation);
            }
            else if (state == RideStates.Dropoff)
            {
                if (endLocation != null && map.MapElements.Contains(endLocation))
                {
                    map.MapElements.Remove(endLocation);
                }
                endLocation = new MapIcon();
                endLocation.Location = position.Coordinate.Point;
                endLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
                endLocation.Title = "Dropoff location";
                map.MapElements.Add(endLocation);
            }
        }

        private LyftConstants.RideType? GetPreferredType(List<RideType> availableRideTypes)
        {
            foreach (var preferredRideType in preferredRideTypes)
            {
                foreach (var availableRideType in availableRideTypes)
                {
                    if (preferredRideType == availableRideType.Type)
                    {
                        return preferredRideType;
                    }
                }
            }
            return null;
        }

        private void SelectPreferredType(LyftConstants.RideType preferredRideType)
        {
            for (int i = 0; i < rideTypeComboBox.Items.Count; i++)
            {
                string text = (string)((ComboBoxItem)rideTypeComboBox.Items[i]).Content;
                foreach (var availableRideType in availableRideTypes)
                {
                    if (availableRideType.DisplayName == text &&
                        availableRideType.Type == preferredRideType)
                    {
                        rideTypeComboBox.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        private string GetDisplayName(LyftConstants.RideType rideType)
        {
            foreach (var availableRideType in availableRideTypes)
            {
                if (availableRideType.Type == rideType)
                {
                    return availableRideType.DisplayName;
                }
            }
            return string.Empty;
        }

        private async Task ShowRoute(BasicGeoposition start, BasicGeoposition end)
        {
            map.Routes.Clear();
            MapRouteFinderResult routeResult = await MapRouteFinder.GetDrivingRouteAsync(new Geopoint(start),
                new Geopoint(end),
                MapRouteOptimization.TimeWithTraffic);

            if (routeResult.Status == MapRouteFinderStatus.Success)
            {
                MapRouteView viewOfRoute = new MapRouteView(routeResult.Route);
                map.Routes.Add(viewOfRoute);
                await map.TrySetViewBoundsAsync(routeResult.Route.BoundingBox, new Thickness(25), MapAnimationKind.Bow);
            }
        }

        private async Task RefreshRideDetails()
        {
            while (rideState == RideStates.Ride)
            {
                if (rideRequest != null)
                {
                    rideDetails = await AppConstants.ShyftClient.RetrieveRideDetails(rideRequest.RideId);
                    rideStatusBackground.Visibility = Visibility.Visible;
                    rideStatusTextBlock.Text = rideDetails.Status.Humanize(LetterCasing.Title);
                    if (rideDetails.Status == LyftConstants.RideStatus.Pending)
                    {
                        // Orange
                        rideStatusBackground.Background = new SolidColorBrush(AppConstants.GetColor("#FF8C00"));
                    }
                    else if (rideDetails.Status == LyftConstants.RideStatus.Accepted)
                    {
                        // Yellow
                        rideStatusBackground.Background = new SolidColorBrush(AppConstants.GetColor("#FFB900"));
                        vehicleInfoGrid.Visibility = Visibility.Visible;
                        vehicleImage.Source = new BitmapImage(new Uri(rideDetails.Vehicle.ImageUrl));
                        vehicleInfoTextBlock.Text = $"{rideDetails.Vehicle.Year} {rideDetails.Vehicle.Color} {rideDetails.Vehicle.Make} {rideDetails.Vehicle.Model}\n" +
                            $"{rideDetails.Vehicle.LicensePlateState} {rideDetails.Vehicle.LicensePlate}";
                        if (!string.IsNullOrEmpty(rideDetails.BeaconColor))
                        {
                            ampColorRectangle.Visibility = Visibility.Visible;
                            ampColorRectangle.Fill = new SolidColorBrush(AppConstants.GetColor(rideDetails.BeaconColor));
                        }
                        else
                        {
                            ampColorRectangle.Visibility = Visibility.Collapsed;
                        }
                        UpdateDriverLocation(rideDetails.Location);
                        if (rideDetails.Origin.EtaSeconds.HasValue)
                        {
                            rideEtaTextBlock.Visibility = Visibility.Visible;
                            rideEtaTextBlock.Text = $"Arrives in {TimeSpan.FromSeconds(rideDetails.Origin.EtaSeconds.Value).Humanize(2)}";
                        }
                    }
                    else if (rideDetails.Status == LyftConstants.RideStatus.Arrived)
                    {
                        // Light Green
                        rideStatusBackground.Background = new SolidColorBrush(AppConstants.GetColor("#00CC6A"));
                        UpdateDriverLocation(rideDetails.Location);
                        UpdateRideEtaInfo(rideDetails.Origin);
                    }
                    else if (rideDetails.Status == LyftConstants.RideStatus.PickedUp)
                    {
                        // Pink
                        rideStatusBackground.Background = new SolidColorBrush(AppConstants.LyftPink);
                        UpdateDriverLocation(rideDetails.Location);
                        UpdateRideEtaInfo(rideDetails.Destination);
                    }
                    else if (rideDetails.Status == LyftConstants.RideStatus.DroppedOff)
                    {
                        // Blue
                        rideStatusBackground.Background = new SolidColorBrush(AppConstants.LyftMulberry);
                        rideState = RideStates.Complete;
                        ratingGrid.Visibility = Visibility.Visible;
                    }
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }

        private void UpdateDriverLocation(CurrentRideLocation rideLocation)
        {
            if (rideLocation != null)
            {
                if (driverLocation == null)
                {
                    driverLocation = new MapIcon();
                    driverLocation.Title = "Driver";
                    driverLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
                    map.MapElements.Add(driverLocation);
                }
                SetPinLocation(driverLocation, rideLocation);
            }
        }

        private void UpdateRideEtaInfo(RideLocation location)
        {
            if (location != null)
            {
                if (location.EtaSeconds.HasValue)
                {
                    rideEtaTextBlock.Visibility = Visibility.Visible;
                    rideEtaTextBlock.Text = $"Arrival in {TimeSpan.FromSeconds(location.EtaSeconds.Value).Humanize(2)}";
                }
            }
        }

        private async void advanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (rideDetails != null)
            {
                LyftConstants.RideStatus rideStatus = LyftConstants.RideStatus.Pending;
                if (rideDetails.Status == LyftConstants.RideStatus.Pending)
                {
                    rideStatus = LyftConstants.RideStatus.Accepted;
                }
                else if (rideDetails.Status == LyftConstants.RideStatus.Accepted)
                {
                    rideStatus = LyftConstants.RideStatus.Arrived;
                }
                else if (rideDetails.Status == LyftConstants.RideStatus.Arrived)
                {
                    rideStatus = LyftConstants.RideStatus.PickedUp;
                }
                else if (rideDetails.Status == LyftConstants.RideStatus.PickedUp)
                {
                    rideStatus = LyftConstants.RideStatus.DroppedOff;
                }
                else if (rideDetails.Status == LyftConstants.RideStatus.DroppedOff)
                {
                    rideStatus = LyftConstants.RideStatus.Pending;
                }
                await AppConstants.ShyftClient.ChangeRideStatus(rideDetails.RideId, rideStatus);
            }
        }

        private async void tip0Button_Click(object sender, RoutedEventArgs e)
        {
            await CompleteRide(GetRating(), GetTipAmount(sender));
        }

        private async void tip1Button_Click(object sender, RoutedEventArgs e)
        {
            await CompleteRide(GetRating(), GetTipAmount(sender));
        }

        private async void tip2Button_Click(object sender, RoutedEventArgs e)
        {
            await CompleteRide(GetRating(), GetTipAmount(sender));
        }

        private async void tip3Button_Click(object sender, RoutedEventArgs e)
        {
            await CompleteRide(GetRating(), GetTipAmount(sender));
        }

        private async void tip4Button_Click(object sender, RoutedEventArgs e)
        {
            await CompleteRide(GetRating(), GetTipAmount(sender));
        }

        private int GetRating()
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)ratingComboBox.SelectedItem;
            return int.Parse((string)comboBoxItem.Content);
        }

        private int GetTipAmount(object sender)
        {
            Button button = (Button)sender;
            string stringAmount = ((string)button.Content).Replace("$", "");
            return int.Parse(stringAmount) * 100;
        }

        private async Task CompleteRide(int rating, int tipAmount)
        {
            await AppConstants.ShyftClient.RateRide(rideDetails.RideId, rating, tipAmount, "USD");
        }
    }
}
