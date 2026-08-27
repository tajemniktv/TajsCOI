// Taj's COI Mods | ConfigurationPayloadCodec.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TajsCOI.Common.Configuration
{
    /// <summary>
    /// Encodes extension payloads into a deterministic, primitive-only text record suitable for
    /// carrying in the native EntityConfigData string bag. This is deliberately independent of
    /// MaFi so Common remains a normal library.
    /// </summary>
    public static class ConfigurationPayloadCodec
    {
        private const string Header = "TajsCOIConfigurationV1";

        public static bool TrySerialize(ConfigurationSnapshot snapshot, out string encoded, out string error)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            error = string.Empty;
            var builder = new StringBuilder(Header).AppendLine();
            try
            {
                foreach (ConfigurationPayload payload in snapshot.Payloads.OrderBy(item => item.HandlerId, StringComparer.Ordinal))
                {
                    foreach (KeyValuePair<string, object> value in payload.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        if (!TryEncodeValue(value.Value, out char type, out string text))
                        {
                            error = "Unsupported configuration value for " + payload.HandlerId + "." + value.Key + ".";
                            encoded = string.Empty;
                            return false;
                        }

                        builder.Append(Encode(payload.HandlerId)).Append('\t')
                            .Append(Encode(payload.Owner)).Append('\t')
                            .Append(payload.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('\t')
                            .Append(Encode(value.Key)).Append('\t')
                            .Append(type).Append('\t')
                            .Append(Encode(text)).AppendLine();
                    }
                }

                encoded = builder.ToString();
                return true;
            }
            catch (EncoderFallbackException exception)
            {
                encoded = string.Empty;
                error = exception.Message;
                return false;
            }
        }

        public static bool TryDeserialize(string encoded, out ConfigurationSnapshot snapshot, out string error)
        {
            snapshot = new ConfigurationSnapshot(Array.Empty<ConfigurationPayload>());
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(encoded))
            {
                error = "Configuration payload is empty.";
                return false;
            }

            try
            {
                string[] lines = encoded.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
                {
                    error = "Unsupported configuration payload schema.";
                    return false;
                }

                var payloads = new Dictionary<string, PayloadBuilder>(StringComparer.Ordinal);
                for (int index = 1; index < lines.Length; index++)
                {
                    string[] fields = lines[index].Split('\t');
                    if (fields.Length != 6 || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema) || schema < 1 ||
                        !TryDecode(fields[0], out string handlerId) || !TryDecode(fields[1], out string owner) ||
                        !TryDecode(fields[3], out string key) || !TryDecode(fields[5], out string text) ||
                        !TryDecodeValue(fields[4], text, out object? value))
                    {
                        error = "Malformed configuration payload record at line " + (index + 1).ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }

                    string payloadKey = handlerId + "\u0000" + owner + "\u0000" + schema.ToString(CultureInfo.InvariantCulture);
                    if (!payloads.TryGetValue(payloadKey, out PayloadBuilder? builder))
                    {
                        builder = new PayloadBuilder(handlerId, owner, schema);
                        payloads.Add(payloadKey, builder);
                    }
                    builder.Values[key] = value!;
                }

                snapshot = new ConfigurationSnapshot(payloads.Values.Select(builder =>
                    new ConfigurationPayload(builder.HandlerId, builder.Owner, builder.SchemaVersion, builder.Values)));
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException || exception is OverflowException)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static bool TryDecode(string value, out string decoded)
        {
            decoded = string.Empty;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool TryEncodeValue(object? value, out char type, out string text)
        {
            switch (value)
            {
                case null: type = 'n'; text = string.Empty; return true;
                case string stringValue: type = 's'; text = stringValue; return true;
                case bool boolValue: type = 'b'; text = boolValue ? "true" : "false"; return true;
                case byte byteValue: type = 'y'; text = byteValue.ToString(CultureInfo.InvariantCulture); return true;
                case sbyte sbyteValue: type = 'Y'; text = sbyteValue.ToString(CultureInfo.InvariantCulture); return true;
                case short shortValue: type = 'h'; text = shortValue.ToString(CultureInfo.InvariantCulture); return true;
                case ushort ushortValue: type = 'H'; text = ushortValue.ToString(CultureInfo.InvariantCulture); return true;
                case int intValue: type = 'i'; text = intValue.ToString(CultureInfo.InvariantCulture); return true;
                case uint uintValue: type = 'I'; text = uintValue.ToString(CultureInfo.InvariantCulture); return true;
                case long longValue: type = 'l'; text = longValue.ToString(CultureInfo.InvariantCulture); return true;
                case ulong ulongValue: type = 'L'; text = ulongValue.ToString(CultureInfo.InvariantCulture); return true;
                case float floatValue: type = 'f'; text = floatValue.ToString("R", CultureInfo.InvariantCulture); return true;
                case double doubleValue: type = 'd'; text = doubleValue.ToString("R", CultureInfo.InvariantCulture); return true;
                case decimal decimalValue: type = 'm'; text = decimalValue.ToString(CultureInfo.InvariantCulture); return true;
                default: type = default; text = string.Empty; return false;
            }
        }

        private static bool TryDecodeValue(string typeText, string text, out object? value)
        {
            value = null;
            if (typeText.Length != 1)
            {
                return false;
            }
            switch (typeText[0])
            {
                case 'n': value = null; return text.Length == 0;
                case 's': value = text; return true;
                case 'b': if (bool.TryParse(text, out bool boolValue)) { value = boolValue; return true; } return false;
                case 'y': if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte byteValue)) { value = byteValue; return true; } return false;
                case 'Y': if (sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sbyteValue)) { value = sbyteValue; return true; } return false;
                case 'h': if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short shortValue)) { value = shortValue; return true; } return false;
                case 'H': if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort ushortValue)) { value = ushortValue; return true; } return false;
                case 'i': if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) { value = intValue; return true; } return false;
                case 'I': if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uintValue)) { value = uintValue; return true; } return false;
                case 'l': if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue)) { value = longValue; return true; } return false;
                case 'L': if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ulongValue)) { value = ulongValue; return true; } return false;
                case 'f': if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) { value = floatValue; return true; } return false;
                case 'd': if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue)) { value = doubleValue; return true; } return false;
                case 'm': if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue)) { value = decimalValue; return true; } return false;
                default: return false;
            }
        }

        private sealed class PayloadBuilder
        {
            internal PayloadBuilder(string handlerId, string owner, int schemaVersion)
            {
                HandlerId = handlerId;
                Owner = owner;
                SchemaVersion = schemaVersion;
            }

            internal string HandlerId { get; }
            internal string Owner { get; }
            internal int SchemaVersion { get; }
            internal Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);
        }
    }
}
