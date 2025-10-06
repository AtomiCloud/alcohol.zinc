using System.Security.Cryptography;
using System.Text;
using App.StartUp.Options;
using CSharp_Result;
using Microsoft.Extensions.Options;

namespace App.Modules.Payment.Airwallex;

public class AirwallexHmacCalculator(IOptions<PaymentOption> options)
{
  public Result<string> Compute(string timestamp, string payload)
  {
    var key = options.Value.Airwallex.Webhook;
    using var hmacsha256 = new HMACSHA256(Encoding.UTF8.GetBytes(key));
    var hash = hmacsha256.ComputeHash(Encoding.UTF8.GetBytes(timestamp + payload));
    return Convert.ToHexString(hash).ToLower();
  }
}
