using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.Storage;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FhirBcda.Function
{

    public static class FhirBcdaOrchestration
    {        
        public class BcdaJob{
            public string authToken { get; set; }
            public string jobLocation { get; set; }
            public Dictionary<string, string> jobs { get; set; }
        }

        [FunctionName("FhirBcdaOrchestration")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();
            var bcdaJob = new BcdaJob();
            var authCred = "3841c594-a8c0-41e5-98cc-38bb45360d3c:d89810016460e6924a1c62583e5f51d1cbf911366c6bc6f040ff9f620a944efbf2b7264afe071609";

            bcdaJob.authToken = await context.CallActivityAsync<string>("FhirBcdaOrchestration_GetAuthToken", authCred);
            outputs.Add(bcdaJob.authToken);

            bcdaJob.jobLocation = await context.CallActivityAsync<string>("FhirBcdaOrchestration_StartJob", bcdaJob.authToken);
            outputs.Add(bcdaJob.jobLocation);

            bcdaJob.jobs = await context.CallActivityAsync<Dictionary<string, string>>("FhirBcdaOrchestration_CheckJobStatus", bcdaJob);
            outputs.Add(bcdaJob.jobs["Patient"].ToString());
            outputs.Add(bcdaJob.jobs["Coverage"].ToString());
            outputs.Add(bcdaJob.jobs["ExplanationOfBenefit"].ToString());

            var patients = await context.CallActivityAsync<string>("FhirBcdaOrchestration_GetPatientData", bcdaJob);
            //var patients = await context.CallActivityAsync<Stream>("FhirBcdaOrchestration_GetPatientData", bcdaJob);

            // // Replace "hello" with the name of your Durable Activity Function.
            // outputs.Add(await context.CallActivityAsync<string>("FhirBcdaOrchestration_Hello", "Tokyo"));
            // outputs.Add(await context.CallActivityAsync<string>("FhirBcdaOrchestration_Hello", "Seattle"));
            // outputs.Add(await context.CallActivityAsync<string>("FhirBcdaOrchestration_Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }

        [FunctionName("FhirBcdaOrchestration_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("FhirBcdaOrchestration_GetAuthToken")]
        public static async Task<string> BcdaGetAuthToken([ActivityTrigger] string authCred, ILogger log)
        {
            log.LogInformation($"Getting Auth Token.");
            
            var apiUrl = "https://sandbox.bcda.cms.gov/auth/token";

            //var authCredential = Encoding.UTF8.GetBytes("{user}:{pwd}");
            var authCredential = Encoding.UTF8.GetBytes(authCred);
            var data = "";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authCredential));
                client.BaseAddress = new Uri(apiUrl);
                HttpResponseMessage response = await client.PostAsync(apiUrl, null);
                if (response.IsSuccessStatusCode)
                {
                    var httpResponseResult = response.Content.ReadAsStringAsync().ContinueWith(task => task.Result).Result;
                    data = JObject.Parse(httpResponseResult)["access_token"].ToString();
                }
            }
            return data;
        }

        [FunctionName("FhirBcdaOrchestration_StartJob")]
        public static async Task<string> BcdaStartJob([ActivityTrigger] string authToken, ILogger log)
        {
            log.LogInformation($"Starting Job.");
            
            var apiUrl = "https://sandbox.bcda.cms.gov/api/v1/Patient/$export";            
            var contentLocation = "";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
                client.DefaultRequestHeaders.Add("Accept","application/fhir+json");
                client.DefaultRequestHeaders.Add("Prefer","respond-async");
                client.BaseAddress = new Uri(apiUrl);
                
                HttpResponseMessage response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    contentLocation = response.Content.Headers.ContentLocation.ToString();
                }
            }
            return contentLocation;
        }

        [FunctionName("FhirBcdaOrchestration_CheckJobStatus")]
        public static async Task<Dictionary<string, string>> BcdaCheckJobStatus(
            [ActivityTrigger] BcdaJob bcdaJob, ILogger log)
        {
            log.LogInformation($"Checking Job Status at {bcdaJob.jobLocation}.");
            
            var apiUrl = bcdaJob.jobLocation;
            var jobs = new Dictionary<string, string>();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bcdaJob.authToken);
                client.DefaultRequestHeaders.Add("Accept","application/json");
                client.BaseAddress = new Uri(apiUrl);
                
                HttpResponseMessage response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var httpResponseResult = response.Content.ReadAsStringAsync().ContinueWith(task => task.Result).Result;
                    var jobUrls = JObject.Parse(httpResponseResult)["output"];
                    
                    foreach(var job in jobUrls)
                    {
                        jobs.Add(job["type"].ToString(), job["url"].ToString());
                    }
                }
            }
            return jobs;
        }

        [FunctionName("FhirBcdaOrchestration_GetPatientData")]
        public static async Task<string> BcdaGetPatientData(
            [ActivityTrigger] BcdaJob bcdaJob,
            ILogger log)
        {
            log.LogInformation($"Get Patient Data at {bcdaJob.jobs["Patient"]}.");
            
            var apiUrl = bcdaJob.jobs["Patient"];
            string patientData = "";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bcdaJob.authToken);
                client.DefaultRequestHeaders.Add("Accept","application/fhir+ndjson");
                client.BaseAddress = new Uri(apiUrl);
                
                HttpResponseMessage response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var httpResponseResult = response.Content.ReadAsStringAsync().ContinueWith(task => task.Result).Result;
                    //patientData = response.Content.ReadAsStreamAsync().ContinueWith(task => task.Result).Result;
                    patientData = JObject.Parse(httpResponseResult).ToString();
                    //patientData = JsonConvert.ToString(JObject.Parse(httpResponseResult));;
                }
            }
            return patientData;
        }

        [FunctionName("FhirBcdaOrchestration_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("FhirBcdaOrchestration", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}