using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Flurl;
using Shyft;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace LyftApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class RegistrationPage : Page
    {
        ApplicationDataContainer localSettings;

        public RegistrationPage()
        {
            this.InitializeComponent();

            localSettings = ApplicationData.Current.LocalSettings;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            string refreshToken = (string)localSettings.Values["refresh_token"];
            if (string.IsNullOrEmpty(refreshToken))
            {
                webView.Visibility = Visibility.Visible;
                List<ShyftConstants.AuthScopes> scopes = new List<ShyftConstants.AuthScopes>()
                {
                    ShyftConstants.AuthScopes.Offline,
                    ShyftConstants.AuthScopes.Profile,
                    ShyftConstants.AuthScopes.Public,
                    ShyftConstants.AuthScopes.RidesRead,
                    ShyftConstants.AuthScopes.RidesRequest
                };
                webView.Navigate(new Uri(AppConstants.ShyftClient.GetAuthUrl(scopes)));
            }
            else
            {
                await AppConstants.ShyftClient.AuthWithRefreshToken(refreshToken);
                Frame.Navigate(typeof(MainPage));
            }
        }

        private async void webView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            if (webView.Source.Host.Contains("golf1052.com"))
            {
                Url url = new Url(webView.Source.ToString());
                string code = (string)url.QueryParams["code"];
                await AppConstants.ShyftClient.Auth(code);
                localSettings.Values["refresh_token"] = AppConstants.ShyftClient.RefreshToken;
                Frame.Navigate(typeof(MainPage));
            }
        }
    }
}
