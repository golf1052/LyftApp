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
            Ride
        }

        private RideStates rideState;
        private Geolocator geolocator;
        private List<MapElement> driverList;
        private MapIcon userLocation;
        private MapIcon startLocation;
        private DateTime? addressLastChecked;
        private PlacesService placesService;
        private PlaceDetailsService placeDetailsService;
        private Dictionary<string, string> addressToId;
        private bool reverseGeocode;
        private BasicGeoposition lastCenter;
        private bool loaded;

        public MainPage()
        {
            this.InitializeComponent();

            rideState = RideStates.Pickup;

            driverList = new List<MapElement>();

            map.MapServiceToken = Secrets.MapToken;
            map.BusinessLandmarksVisible = true;
            map.LandmarksVisible = true;
            map.PedestrianFeaturesVisible = true;
            map.TrafficFlowVisible = true;
            map.TransitFeaturesVisible = true;

            geolocator = new Geolocator();
            geolocator.AllowFallbackToConsentlessPositions();
            geolocator.ReportInterval = (uint)TimeSpan.FromSeconds(15).TotalSeconds;

            geolocator.PositionChanged += Geolocator_PositionChanged;

            placesService = new PlacesService();
            placeDetailsService = new PlaceDetailsService();
            addressToId = new Dictionary<string, string>();
            reverseGeocode = true;
            loaded = false;
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
            userLocation.NormalizedAnchorPoint = new Point(0.5, 1.0);
            userLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            userLocation.Title = "Your location";
            map.MapElements.Add(userLocation);

            startLocation = new MapIcon();
            startLocation.Location = position.Coordinate.Point;
            startLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            startLocation.Title = "Pickup location";
            map.MapElements.Add(startLocation);

            UpdateFromMapCenterTimer();
            await DisplayDrivers(map.Center.Position, RideTypeEnum.RideTypes.Lyft);
            await GetEta(map.Center.Position, RideTypeEnum.RideTypes.Lyft);
            loaded = true;
        }

        private async Task UpdateFromMapCenterTimer()
        {
            while (true)
            {
                if (rideState == RideStates.Pickup)
                {
                    if (!map.Center.Position.EqualsOther(lastCenter))
                    {
                        await ReverseGeocode(new Geopoint(map.Center.Position));
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private async Task DisplayDrivers(BasicGeoposition geoposition, RideTypeEnum.RideTypes rideType)
        {
            var nearbyDrivers = await AppConstants.ShyftClient.RetrieveNearbyDrivers(geoposition.Latitude, geoposition.Longitude);
            foreach (var driver in driverList)
            {
                map.MapElements.Remove(driver);
            }
            driverList.Clear();
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

        private async Task GetEta(BasicGeoposition geoposition, RideTypeEnum.RideTypes rideType)
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

        private MapIcon CreatePin(LatLng latLng)
        {
            MapIcon icon = new MapIcon();
            icon.Location = new Geopoint(new BasicGeoposition()
            {
                Latitude = latLng.Lat,
                Longitude = latLng.Lng
            });
            icon.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            return icon;
        }

        private async void map_CenterChanged(MapControl sender, object args)
        {
            if (startLocation != null)
            {
                startLocation.Location = map.Center;

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

        private async Task ReverseGeocode(Geopoint geopoint)
        {
            MapLocationFinderResult result = await MapLocationFinder.FindLocationsAtAsync(geopoint);
            if (result.Status == MapLocationFinderStatus.Success && result.Locations.Count > 0)
            {
                pickupSearchBox.Text = result.Locations[0].DisplayName;
                lastCenter = geopoint.Position;
            }
        }

        private async void pickupSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                if (pickupSearchBox.Text.Trim().Length >= 2)
                {
                    AutocompleteRequest autocompleteRequest = new AutocompleteRequest();
                    autocompleteRequest.Input = pickupSearchBox.Text.Trim();
                    autocompleteRequest.Location = new Google.Maps.LatLng(map.Center.Position.Latitude, map.Center.Position.Longitude);
                    AutocompleteResponse autocompleteResponse = await placesService.GetAutocompleteResponseAsync(autocompleteRequest);
                    if (autocompleteResponse.Status == Google.Maps.ServiceResponseStatus.Ok)
                    {
                        pickupSearchBox.Items.Clear();
                        addressToId.Clear();
                        foreach (var prediction in autocompleteResponse.Predictions)
                        {
                            pickupSearchBox.Items.Add(prediction.description);
                            addressToId.Add(prediction.description, prediction.PlaceId);
                        }
                    }
                }
            }
        }

        private void pickupSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            pickupSearchBox.Text = (string)args.SelectedItem;
        }

        private void pickupSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (addressToId.ContainsKey(pickupSearchBox.Text))
            {
                PlaceDetailsRequest placeDetailsRequest = new PlaceDetailsRequest();
                placeDetailsRequest.PlaceID = addressToId[pickupSearchBox.Text];
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
                RideTypeEnum.RideTypes? selectedRideType = null;
                if (e.AddedItems.Count > 0)
                {
                    string selected = (string)((ComboBoxItem)e.AddedItems[0]).Content;
                    if (selected == "Line")
                    {
                        selectedRideType = RideTypeEnum.RideTypes.LyftLine;
                    }
                    else if (selected == "Lyft")
                    {
                        selectedRideType = RideTypeEnum.RideTypes.Lyft;
                    }
                    else if (selected == "Plus")
                    {
                        selectedRideType = RideTypeEnum.RideTypes.LyftPlus;
                    }

                    if (selectedRideType.HasValue)
                    {
                        await DisplayDrivers(map.Center.Position, selectedRideType.Value);
                        await GetEta(map.Center.Position, selectedRideType.Value);
                    }
                }
            }
        }
    }
}
