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

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace LyftApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private Geolocator geolocator;
        private List<MapElement> driverLayer;
        private MapIcon startLocation;
        private DateTime? addressLastChecked;
        private PlacesService placesService;
        private PlaceDetailsService placeDetailsService;
        private Dictionary<string, string> addressToId;
        private bool reverseGeocode;

        public MainPage()
        {
            this.InitializeComponent();

            driverLayer = new List<MapElement>();

            map.MapServiceToken = Secrets.MapToken;
            map.BusinessLandmarksVisible = true;
            map.LandmarksVisible = true;
            map.PedestrianFeaturesVisible = true;
            map.TrafficFlowVisible = true;
            map.TransitFeaturesVisible = true;

            geolocator = new Geolocator();
            geolocator.AllowFallbackToConsentlessPositions();
            geolocator.ReportInterval = (uint)TimeSpan.FromSeconds(15).TotalSeconds;

            placesService = new PlacesService();
            placeDetailsService = new PlaceDetailsService();
            addressToId = new Dictionary<string, string>();
            reverseGeocode = true;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            GeolocationAccessStatus accessStatus = await Geolocator.RequestAccessAsync();
            //if (accessStatus == GeolocationAccessStatus.Unspecified)
            //{
            //    // don't actually do this
            //    throw new Exception();
            //}

            Geolocator geolocator = new Geolocator();
            geolocator.AllowFallbackToConsentlessPositions();
            geolocator.ReportInterval = (uint)TimeSpan.FromSeconds(15).TotalSeconds;
            Geoposition position = await geolocator.GetGeopositionAsync();
            map.Center = position.Coordinate.Point;
            map.ZoomLevel = 15;
            List<MapElement> layer = new List<MapElement>();
            MapIcon location = new MapIcon();
            location.Location = position.Coordinate.Point;
            location.NormalizedAnchorPoint = new Point(0.5, 1.0);
            location.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            location.Title = "Your location";
            layer.Add(location);
            MapElementsLayer mapElementsLayer = new MapElementsLayer();
            mapElementsLayer.MapElements = layer;
            map.Layers.Add(mapElementsLayer);

            startLocation = new MapIcon();
            startLocation.Location = position.Coordinate.Point;
            startLocation.CollisionBehaviorDesired = MapElementCollisionBehavior.RemainVisible;
            startLocation.Title = "Pickup location";
            map.Layers.Add(new MapElementsLayer()
            {
                MapElements = new List<MapElement>()
                {
                    startLocation
                }
            });

            await DisplayDrivers();
        }

        private async Task DisplayDrivers()
        {
            Geoposition position = await geolocator.GetGeopositionAsync();
            Geopoint point = position.Coordinate.Point;
            var nearbyDrivers = await AppConstants.ShyftClient.RetrieveNearbyDrivers(point.Position.Latitude, point.Position.Longitude);
            driverLayer.Clear();
            foreach (var nearbyDriver in nearbyDrivers)
            {
                foreach (var driver in nearbyDriver.Drivers)
                {
                    driverLayer.Add(CreatePin(driver.Locations.Last()));
                }
            }

            map.Layers.Add(new MapElementsLayer()
            {
                MapElements = driverLayer
            });
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
                        MapLocationFinderResult result = await MapLocationFinder.FindLocationsAtAsync(map.Center);
                        if (result.Status == MapLocationFinderStatus.Success && result.Locations.Count > 0)
                        {
                            pickupSearchBox.Text = result.Locations[0].DisplayName;
                        }
                    }
                    else
                    {
                        reverseGeocode = true;
                    }
                }
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
    }
}
