using System;
using System.Threading;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Web;
using System.Net;
using System.Text;

namespace TmobileRefresh
{
    class Program
    {
        static string login;
        static string password;

        static string authorizationCode;
        static string accessToken;
        static string subscriptionUrl;
        //I'm only using the A0DAY01 bundle, and can't test for other bundles.
        static string bundle = "A0DAY01";
        //assuming speed of 25 MB/s
        static int speed = 25;

        static async System.Threading.Tasks.Task Main(string[] args)
        {
            //load username and password from command line
            if(args.Length<2)
            {
                Console.WriteLine("Please add username and password");
                return;
            } else
            {
                login = args[0];
                password = args[1];
            }

            //do initial authorization token request
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("T-Mobile 5.3.28 (Android 10; 10)");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "OWhhdnZhdDZobTBiOTYyaTo=");

            //set post body
            var values = new Dictionary<string, string>
            {
                {"Username", login},
                {"Password", password},
                {"ClientId", "9havvat6hm0b962i"},
                { "Scope", "usage+readfinancial+readsubscription+readpersonal+readloyalty+changesubscription+weblogin"}
            };
            var content = new FormUrlEncodedContent(values);

            //create post request to get the authorization code
            var response = await client.PostAsync("https://capi.t-mobile.nl/login?response_type=code", content);

            //Get the authorization code from the response
            IEnumerable<string> headervalues;
            response.Headers.TryGetValues("AuthorizationCode", out headervalues);
            if(headervalues == null)
            {
                Console.WriteLine("Could not get the authorization code, are the username and password correct?");
                return;
            }
            authorizationCode = headervalues.FirstOrDefault();

            // get access token
            // set post body
            values = new Dictionary<string, string>
            {
                {"AuthorizationCode", authorizationCode }
            };
            content = new FormUrlEncodedContent(values);

            //create post request to get the access token
            response = await client.PostAsync("https://capi.t-mobile.nl/createtoken", content);

            //get the access token from the response
            response.Headers.TryGetValues("AccessToken", out headervalues);
            accessToken = headervalues.FirstOrDefault();

            // get linked subscription url
            // reset the http client
            client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("T-Mobile 5.3.28 (Android 10; 10)");

            //Do a get request to get the subscriptions for this access token
            response = await client.GetAsync("https://capi.t-mobile.nl/account/current?resourcelabel=LinkedSubscriptions");
            
            //get the json body from the response
            string httpcontent = "";
            // We get redirects that lose our authorization header. If that's the case, we repeat the request with the header again
            while (!response.IsSuccessStatusCode)
            {
                var uri = response.RequestMessage.RequestUri;
                response = await client.GetAsync(uri);
                httpcontent = await response.Content.ReadAsStringAsync();
            }
            JObject array = JsonConvert.DeserializeObject<JObject>(httpcontent);
            string newurl = (string) array.SelectToken("Resources[0].Url");

            //get subscription url 

            response = await client.GetAsync(newurl);
            httpcontent = await response.Content.ReadAsStringAsync();

            array = JsonConvert.DeserializeObject<JObject>(httpcontent);
            subscriptionUrl = (string)array.SelectToken("subscriptions[0].SubscriptionURL");

            int sleeptimer=60000;

            while (true)
            {
                //get data
                //client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
                response = await client.GetAsync(subscriptionUrl + "/roamingbundles");
                httpcontent = await response.Content.ReadAsStringAsync();
                

                array = JsonConvert.DeserializeObject<JObject>(httpcontent);
                //extract how much data we have left in our bundle
                int remaining = (int)array.SelectToken("Bundles[1].Remaining.Value");
                Console.WriteLine("Remaining: " + remaining);

                //if the bundle is 0, we underestimated our speed
                if(remaining == 0)
                {
                    speed++;
                }

                // If our  response body does not contain the bundle, we havent added it yet today
                if (!httpcontent.Contains(bundle))
                {
                    await TopUp();
                    remaining = 2000000;
                }
                // If we have it in the body, we need to check how much we have remaining. If there's less than 400 MB, top up
                else
                {
                    //Depending on how much we have left in the bundle, we either want to top up or sleep until the bundle is running out
                    if (remaining < 400000)
                    {
                        await TopUp();
                        remaining = 2000000;
                    }
                    // depending on how long this bundle will last for sure, sleep
                    sleeptimer = remaining / speed;
                }
                Console.WriteLine("Sleeping for "+ sleeptimer/1000 +" seconds");
                Thread.Sleep(sleeptimer);
            }
        }

        static async System.Threading.Tasks.Task TopUp()
        {
            //Making our message body to request the bundle
            string jsontext = "{\"Bundles\":[{\"BuyingCode\":\"REPLACEME\"}]}".Replace("REPLACEME", bundle);

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("T-Mobile 5.3.28 (Android 10; 10)");

            Console.WriteLine("Need new package");
            client.BaseAddress = new Uri(subscriptionUrl + "/");
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "roamingbundles");
            request.Content = new StringContent(jsontext, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.SendAsync(request);
            var body = await response.RequestMessage.Content.ReadAsStringAsync();
            Console.WriteLine("The HTTP request returned: " + response.StatusCode);
        }
    }
}
