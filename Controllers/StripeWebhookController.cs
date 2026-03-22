using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        AppDbContext db,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        string json;

        using (var reader = new StreamReader(HttpContext.Request.Body))
        {
            json = await reader.ReadToEndAsync();
        }

        try
        {
            var webhookSecret = GetSetting("Stripe:WebhookSecret", "Stripe__WebhookSecret");
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                _logger.LogError("Stripe webhook secret mancante.");
                return StatusCode(500, "Configurazione Stripe mancante");
            }

            var stripeSignature = Request.Headers["Stripe-Signature"].ToString();
            if (string.IsNullOrWhiteSpace(stripeSignature))
            {
                _logger.LogWarning("Header Stripe-Signature mancante.");
                return BadRequest("Firma webhook mancante");
            }

            var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);

            if (stripeEvent.Type != "checkout.session.completed")
            {
                return Ok();
            }

            var session = stripeEvent.Data.Object as Session;
            if (session == null)
            {
                _logger.LogWarning("Session Stripe nulla o non valida.");
                return BadRequest("Sessione Stripe non valida");
            }

            var customerEmail = session.CustomerDetails?.Email ?? session.CustomerEmail;
            if (string.IsNullOrWhiteSpace(customerEmail))
            {
                _logger.LogWarning("Email cliente non trovata per session {SessionId}.", session.Id);
                return BadRequest("Email cliente non trovata");
            }

            var licenseKey = BuildLicenseKey(session.Id);

            var existingLicense = await _db.Licenses
                .FirstOrDefaultAsync(x => x.LicenseKey == licenseKey);

            if (existingLicense != null)
            {
                _logger.LogInformation(
                    "Webhook duplicato o già processato. SessionId: {SessionId}, LicenseKey: {LicenseKey}",
                    session.Id,
                    licenseKey);

                return Ok();
            }

            var license = new License
            {
                LicenseKey = licenseKey,
                IsActive = true
            };

            _db.Licenses.Add(license);
            await _db.SaveChangesAsync();

            await SendLicenseEmail(customerEmail, licenseKey);

            _logger.LogInformation(
                "Licenza creata e inviata. SessionId: {SessionId}, Email: {Email}, LicenseKey: {LicenseKey}",
                session.Id,
                customerEmail,
                licenseKey);

            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Errore Stripe webhook.");
            return BadRequest("Webhook Stripe non valido");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore interno nel webhook Stripe.");
            return StatusCode(500, "Errore interno");
        }
    }

    private async Task SendLicenseEmail(string toEmail, string licenseKey)
    {
        var apiKey = GetSetting("Brevo:ApiKey", "Brevo__ApiKey");
        var fromEmail = GetSetting("Brevo:FromEmail", "Brevo__FromEmail");
        var fromName = GetSetting("Brevo:FromName", "Brevo__FromName");

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(fromEmail) ||
            string.IsNullOrWhiteSpace(fromName))
        {
            throw new Exception("Configurazione Brevo mancante");
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            sender = new
            {
                name = fromName,
                email = fromEmail
            },
            to = new[]
            {
                new { email = toEmail }
            },
            subject = "La tua licenza Licensio",
            htmlContent = $@"
                <html>
                  <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #222;'>
                    <h2>Pagamento ricevuto</h2>
                    <p>Grazie per il tuo acquisto.</p>
                    <p>La tua licenza è:</p>
                    <p style='font-size: 20px; font-weight: bold; letter-spacing: 1px;'>{licenseKey}</p>
                    <p>Conserva questa email per riferimento futuro.</p>
                  </body>
                </html>"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync("https://api.brevo.com/v3/smtp/email", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Errore invio email Brevo: {responseBody}");
        }
    }

    private string GetSetting(string configKey, string envKey)
    {
        var envValue = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue.Trim();

        var configValue = _config[configKey];
        if (!string.IsNullOrWhiteSpace(configValue))
            return configValue.Trim();

        return string.Empty;
    }

    private string BuildLicenseKey(string sessionId)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
        var hex = Convert.ToHexString(hash);

        return "LIC-" + hex[..16];
    }
}