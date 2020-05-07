using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Collections.Generic;
using System.Text.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;

namespace Zetill.Utils
{
    public class SendEmail
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<SendEmail> log;

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

            string requestBody;
            SendEmailRequest request;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync().ConfigureAwait(false);
                request = JsonSerializer.Deserialize<SendEmailRequest>(requestBody);
            }
            catch (JsonException jEx)
            {
                log.LogError(jEx, "Unable to deserialize request object.");
                return new BadRequestResult();
            }
            catch(ArgumentNullException ex)
            {
                log.LogError(ex, "Request object is null.");
                return new BadRequestResult();
            }

            var hCaptchaSecret = GetSecretWithName("HCaptcha:Secret");

            IEnumerable<KeyValuePair<string, string>> hCaptchaParams = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("secret", hCaptchaSecret),
                new KeyValuePair<string, string>("response",request.HCaptchaChallengeResponse),
                // new KeyValuePair<string, string>("remoteip", /*Client's IP*/), // Optional.
            };

            var hCaptchaVerificationContent = new FormUrlEncodedContent(hCaptchaParams);
            var responseFromHCaptcha = await this.httpClient.PostAsync(@"https://hcaptcha.com/siteverify", hCaptchaVerificationContent).ConfigureAwait(false);

            if (!responseFromHCaptcha.IsSuccessStatusCode)
            {
                this.log.LogWarning($"Captcha validation was unsuccessfull. Response Status Code was: {responseFromHCaptcha.StatusCode}. Reason: {responseFromHCaptcha.ReasonPhrase}");
                return new BadRequestResult();
            }

            // var sourceDomainName = Environment.GetEnvironmentVariable("Email:Sender:DomainName");
            var sourceUserName = Environment.GetEnvironmentVariable("Email:Sender:UserName");
            var sourceEmail = Environment.GetEnvironmentVariable("Email:Sender:Address");

            var targetDomainName = Environment.GetEnvironmentVariable("Email:Destination:DomainName");
            var targetUserName = Environment.GetEnvironmentVariable("Email:Destination:UserName");
            var targetEmail = Environment.GetEnvironmentVariable("Email:Destination:Address");



            var apiKey = GetSecretWithName("SendGrid:Key");
            var sendgridClient = new SendGridClient(apiKey);
            var from = new EmailAddress(sourceEmail, sourceUserName);

            var subject = "Nuevo mensaje recibido";
            var to = new EmailAddress(targetEmail, targetUserName);

            var plainTextContent = $@"Le comunicamos que ha recibido una nueva petición de contacto por medio de su sitio: {targetDomainName}."
                                 + $@"Le mensaje fue enviado por: {request.Name} y dice lo siguiente: {request.Message}."
                                 + $@"Información de contacto: Email: {request.Email} Número de Teléfono: {request.PhoneNumber}.";

            var htmlContent = ""; // "<strong>and easy to do anywhere, even with C#</strong>"; // TODO: Consider using HTML
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var sendgridResponse = await sendgridClient.SendEmailAsync(msg).ConfigureAwait(false);

            if (sendgridResponse.StatusCode != HttpStatusCode.Accepted)
            {
                var responseBody = await sendgridResponse.Body.ReadAsStringAsync().ConfigureAwait(false);
                this.log.LogError($"Unable to send email. Respose from SendGrid was: {responseBody}");
            }

            return new OkObjectResult("Success");
        }

        public string GetSecretWithName(string secretName){
            string keyVaultName = Environment.GetEnvironmentVariable("KeyVaultName");

            if(!keyVaultName.Equals("local", StringComparison.OrdinalIgnoreCase) ){
                var kvUri = "https://" + keyVaultName + ".vault.azure.net";
                var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
                KeyVaultSecret secret = client.GetSecret(secretName);

                return secret.Value;
            }

            // For local development, read secrets from secrets.json settings file.
            return Environment.GetEnvironmentVariable(secretName);
        }
    }
}
