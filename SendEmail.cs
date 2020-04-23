using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Collections.Generic;

namespace Zetill.Utils
{
    public class SendEmail
    {
        private readonly HttpClient httpClient;

        public SendEmail(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        [FunctionName("SendEmail")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<SendEmailRequest>(requestBody);

            var hCaptchaSecret = "";

            IEnumerable<KeyValuePair<string, string>> hCaptchaParams = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("secret", hCaptchaSecret),
                new KeyValuePair<string, string>("response",request.HCaptchaChallengeResponse),
                // new KeyValuePair<string, string>("remoteip", /*Client's IP*/), // Optional.
            };

            var hCaptchaVerificationContent = new FormUrlEncodedContent(hCaptchaParams);
            var responseFromHCaptcha = await this.httpClient.PostAsync(@"https://hcaptcha.com/siteverify", hCaptchaVerificationContent);

            if(!responseFromHCaptcha.IsSuccessStatusCode){
                return new BadRequestResult();
            }

            // TODO: Add logic to format email and call SendGrid.

            return new OkObjectResult("Success");
        }
    }
}
