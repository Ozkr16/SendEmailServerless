using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Collections.Generic;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net;
using System.Text;
using System.Linq;

namespace Zetill.Utils
{
    public class SendEmail
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<SendEmail> log;

        // This works as a memory cache. If the function instance is idle for 5 min or more
        // it will be destroyed, then the "cache" will be cleared.
        private static string internalTemplate;

        public SendEmail(HttpClient httpClient, ILogger<SendEmail> log)
        {
            this.httpClient = httpClient;
            this.log = log;
        }

        [FunctionName("SendEmail")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            
            var separator = Environment.GetEnvironmentVariable("Mail:ParameterSeparatorChar");
            if(string.IsNullOrWhiteSpace(separator) || separator.Length > 1){
                log.LogWarning("Provided separator was invalid, falling back to default comma separator.");
                separator = ","; // Invalid operator fallsback to comma.
            }

            var expectedParameters = Environment.GetEnvironmentVariable("Mail:ExpectedParameters")?.Split(separator);
            if(expectedParameters == null || !expectedParameters.Any()){
                log.LogInformation("There we no expected parameters. Assuming that's ok if email to send is too generic.");
            }


            var form = await req.ReadFormAsync().ConfigureAwait(false);

            if(internalTemplate == null )
            {
                var url = Environment.GetEnvironmentVariable("Mail:TemplateLocationUrl");
                try
                {
                    internalTemplate = await this.httpClient.GetStringAsync(url).ConfigureAwait(false);
                }
                catch(HttpRequestException ex)
                {
                    log.LogError($"Unable to get the email template from url: {url}. Exception: {ex}");
                    return new BadRequestObjectResult("Unable to get the email template. Ask and administrator to update the configuration.");
                }
            }

            var htmlContentBuilder = new StringBuilder(internalTemplate);
            foreach (var expectedParameter in expectedParameters)
            {
                if(form.TryGetValue(expectedParameter, out var parameterValue)){
                    htmlContentBuilder.Replace("{" + expectedParameter + "}", parameterValue);
                }
                else
                {
                    htmlContentBuilder.Replace("{" + expectedParameter + "}", string.Empty);
                    this.log.LogInformation($"Expected parameter was not found in request. Using empty value instead. Param: {expectedParameter}");
                }
            }
            var hCaptchaResponseWasProvided = form.TryGetValue("h-captcha-response", out var hCaptchaChallengeResponse);
            if (!hCaptchaResponseWasProvided)
            {
                log.LogWarning("h-captcha-response was not provided.");
                return new BadRequestObjectResult("HCaptcha challenge response was not provided.");
            }

            var hCaptchaSecret = Environment.GetEnvironmentVariable("HCaptcha:Secret");
            var hCaptchaVerificationEndpoint = Environment.GetEnvironmentVariable("HCaptcha:VerificationEndpoint"); // @"https://hcaptcha.com/siteverify"
            if(string.IsNullOrWhiteSpace(hCaptchaSecret) || string.IsNullOrWhiteSpace(hCaptchaVerificationEndpoint)){
                log.LogError("HCaptcha secret or verification url were not properly set or cannot be retrieved.");
                return new StatusCodeResult(500);
            }

            IEnumerable<KeyValuePair<string, string>> hCaptchaParams = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("secret", hCaptchaSecret),
                new KeyValuePair<string, string>("response",hCaptchaChallengeResponse),
                // new KeyValuePair<string, string>("remoteip", /*Client's IP*/), // Optional.
            };

            var hCaptchaVerificationContent = new FormUrlEncodedContent(hCaptchaParams);
            var responseFromHCaptcha = await this.httpClient.PostAsync(hCaptchaVerificationEndpoint, hCaptchaVerificationContent).ConfigureAwait(false);

            if (!responseFromHCaptcha.IsSuccessStatusCode)
            {
                this.log.LogWarning($"Captcha validation was unsuccessfull. Response Status Code was: {responseFromHCaptcha.StatusCode}. Reason: {responseFromHCaptcha.ReasonPhrase}");
                return new BadRequestObjectResult("Captcha validation was unsuccessfull.");
            }

            var sourceUserName = Environment.GetEnvironmentVariable("Sender:UserName");
            var sourceEmail = Environment.GetEnvironmentVariable("Sender:Address");
            var targetDomainName = Environment.GetEnvironmentVariable("Destination:DomainName");
            var targetUserName = Environment.GetEnvironmentVariable("Destination:UserName");
            var targetEmail = Environment.GetEnvironmentVariable("Destination:Address");
            var emailSubject = Environment.GetEnvironmentVariable("Mail:Subject");

            var hasInvalidConfig = 
               string.IsNullOrWhiteSpace(sourceUserName)
            || string.IsNullOrWhiteSpace(sourceEmail)
            || string.IsNullOrWhiteSpace(targetDomainName)
            || string.IsNullOrWhiteSpace(targetUserName)
            || string.IsNullOrWhiteSpace(targetEmail)
            || string.IsNullOrWhiteSpace(emailSubject);

            if(hasInvalidConfig){
                log.LogError("Email configuration was not properly set or cannot be retrieved.");
                return new StatusCodeResult(500);
            }

            var from = new EmailAddress(sourceEmail, sourceUserName);
            var to = new EmailAddress(targetEmail, targetUserName);

            var plainTextContent = "";

            var msg = MailHelper.CreateSingleEmail(from, to, emailSubject, plainTextContent, htmlContentBuilder.ToString());

            var fileNames = Environment.GetEnvironmentVariable("Mail:ExpectedFiles")?.Split(separator);
            if(fileNames != null && fileNames.Length > 0)
            {
                var taskCollection = new List<Task>();

                foreach (var fileGroup in req.Form.Files
                .Where(f => fileNames.Any(ef => ef.Equals(f.Name, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(f => f.Name))
                {
                    var counter = 1;
                    foreach(var file in fileGroup){
                        var fileExtension = TranslateContentTypeIntoExtension(file.ContentType);
                        if(!string.IsNullOrWhiteSpace(fileExtension))
                        {
                            var fileUploadStream = file.OpenReadStream();
                            var effectiveCounter = counter > 1 ? counter.ToString() : string.Empty;
                            var task = msg.AddAttachmentAsync($"{file.Name}{effectiveCounter}.{fileExtension}", fileUploadStream, file.ContentType);
                            taskCollection.Add(task);
                        }
                        else
                        {
                            log.LogWarning("User tried to upload an unsupported file type.");
                            return new BadRequestObjectResult("Invalid file type.");
                        }
                        counter++;
                    }
                }
                await Task.WhenAll(taskCollection);
            }

            var apiKey = Environment.GetEnvironmentVariable("SendGrid:Key");
            if (string.IsNullOrWhiteSpace(apiKey)){
                log.LogError("Sendgrid config was not properly set.");
                return new StatusCodeResult(500);
            }
            var sendgridClient = new SendGridClient(apiKey);
            var sendgridResponse = await sendgridClient.SendEmailAsync(msg).ConfigureAwait(false);

            if (sendgridResponse.StatusCode != HttpStatusCode.Accepted)
            {
                var responseBody = await sendgridResponse.Body.ReadAsStringAsync().ConfigureAwait(false);
                this.log.LogError($"Unable to send email. Respose from SendGrid was: {responseBody}");

                return new BadRequestObjectResult("Cannot send message.");
            }

            return new OkObjectResult("Success");
        }

        private string TranslateContentTypeIntoExtension(string contentType)
        {
            switch (contentType)
            {
                case "application/msword":
                return "doc";
                case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
                return "docx";
                case "image/jpeg":
                return "jpg";
                case "image/png":
                return "png";
                case "application/pdf":
                return "pdf";
                case "application/vnd.ms-excel":
                return "xls";
                case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
                return "xlsx";
                default:
                return null;
            }
        }
    }
}
