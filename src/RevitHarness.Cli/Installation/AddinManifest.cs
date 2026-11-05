#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RevitHarness.Cli.Installation
{
    /// <summary>
    /// The harness .addin manifest. Its VendorId is the identity hot-loaded code runs under, which is what
    /// Extensible Storage schemas with a vendor id are checked against. Revit reads it at startup.
    /// </summary>
    public static class AddinManifest
    {
        public static string? ReadVendorId(string manifestPath)
        {
            try { return XDocument.Load(manifestPath).Descendants("VendorId").FirstOrDefault()?.Value; }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException) { return null; }
        }

        /// <summary>Returns the refusal message for an unusable VendorId, or null.</summary>
        public static string? CheckVendorId(string vendorId)
        {
            if (string.IsNullOrWhiteSpace(vendorId) || vendorId != vendorId.Trim() || vendorId.Any(char.IsControl))
                return "--vendor-id must be a non-empty single line without leading or trailing spaces.";
            return null;
        }

        /// <summary>Writes the manifest template, with <paramref name="vendorId"/> when given (the template's own id otherwise).</summary>
        public static void Write(Stream template, string destination, string? vendorId)
        {
            var document = XDocument.Load(template);
            if (vendorId != null)
            {
                var element = document.Descendants("VendorId").FirstOrDefault()
                    ?? throw new InvalidOperationException("The packaged RevitHarness.addin has no VendorId element.");
                element.Value = vendorId;
            }
            // A torn write would make the next reinstall read no VendorId and fall back to the packaged one.
            var temporary = destination + ".tmp";
            document.Save(temporary);
            File.Move(temporary, destination, overwrite: true);
        }
    }
}
