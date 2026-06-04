using System;
using System.Collections.Generic;
using System.Text;
using CopilotDeepSeek.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotDeepSeekTests.ConstantsTests
{
    [TestClass]
    public class MimeTypesTests
    {
        [TestMethod]
        public void MimeTypes_ShouldContainExpectedValues()
        {
            // Arrange
            var expectedMimeTypes = new Dictionary<string, string>
            {
                { ".html", "text/html; charset=utf-8" },
                { ".css",  "text/css" },
                { ".js",   "application/javascript" },
                { ".json", "application/json" },
                { ".png",  "image/png" },
                { ".jpg",  "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".gif",  "image/gif" },
                { ".svg",  "image/svg+xml" },
                { ".ico",  "image/x-icon" },
                { ".woff", "font/woff" },
                { ".woff2","font/woff2" },
                { ".ttf",  "font/ttf" },
                { ".map",  "application/json" },
            };
            // Act & Assert
            foreach (var kvp in expectedMimeTypes)
            {
                string extension = kvp.Key;
                string expectedMimeType = kvp.Value;
                string? actualMimeType = MimeTypes.StaticMimeTypes.TryGetValue(extension, out var mimeType) ? mimeType : null;
                Assert.AreEqual(expectedMimeType, actualMimeType, $"MIME type for extension '{extension}' should be '{expectedMimeType}' but was '{actualMimeType}'.");
            }
        }
    }
}
