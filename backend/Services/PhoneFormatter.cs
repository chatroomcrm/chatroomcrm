using System;
using System.Text;

namespace ChatFlowCrm.Services
{
    public static class PhoneFormatter
    {
        public static string Format(string phone, string defaultCountryCode = "+91")
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

            string cleaned = phone.Trim();

            // 1. If it contains a dash, split and extract country code and subscriber number
            if (cleaned.Contains('-'))
            {
                var parts = cleaned.Split('-');
                if (parts.Length >= 2)
                {
                    string country = CleanNonDigitsKeepPlus(parts[0]);
                    string subscriber = CleanOnlyDigits(parts[1]);
                    
                    if (!country.StartsWith("+") && !string.IsNullOrEmpty(country))
                    {
                        country = "+" + country;
                    }
                    
                    return country + subscriber;
                }
            }

            // 2. Otherwise, clean all formatting characters (keep '+' if present)
            cleaned = CleanNonDigitsKeepPlus(cleaned);

            // 3. If it starts with '+' or already has country code (length > 10 and starts with '+' or '91')
            if (cleaned.StartsWith("+"))
            {
                return cleaned;
            }

            // 4. If it's a 10-digit number, prepend the configured default country code
            if (cleaned.Length == 10)
            {
                string prefix = defaultCountryCode.Trim();
                if (!prefix.StartsWith("+"))
                {
                    prefix = "+" + prefix;
                }
                return prefix + cleaned;
            }

            // 5. If it is longer than 10 digits and has no '+' sign, it represents a full international number. Prepend '+' to standardise it.
            if (cleaned.Length > 10)
            {
                return "+" + cleaned;
            }

            return cleaned;
        }

        private static string CleanNonDigitsKeepPlus(string val)
        {
            if (string.IsNullOrEmpty(val)) return string.Empty;
            var sb = new StringBuilder();
            foreach (char c in val)
            {
                if (char.IsDigit(c) || (c == '+' && sb.Length == 0))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string CleanOnlyDigits(string val)
        {
            if (string.IsNullOrEmpty(val)) return string.Empty;
            var sb = new StringBuilder();
            foreach (char c in val)
            {
                if (char.IsDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
