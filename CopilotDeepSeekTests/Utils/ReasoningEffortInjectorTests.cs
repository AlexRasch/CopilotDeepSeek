using Microsoft.VisualStudio.TestTools.UnitTesting;
using CopilotDeepSeek.Utils;
using System.Text.Json;

namespace CopilotDeepSeekTests.Utils
{
    [TestClass]
    public class ReasoningEffortInjectorTests
    {
        [TestMethod]
        public void Inject_ModelContainsMax_StripsSuffixAndAddsReasoningEffort()
        {
            // Arrange
            var body = """{"model":"deepseek-chat:max","messages":[{"role":"user","content":"hello"}]}""";

            // Act
            var result = ReasoningEffortInjector.Inject(body);

            // Assert
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            Assert.AreEqual("deepseek-chat", root.GetProperty("model").GetString());
            Assert.AreEqual("max", root.GetProperty("reasoning_effort").GetString());
            // Ensure original message property is preserved
            Assert.IsTrue(root.TryGetProperty("messages", out _));
        }

        [TestMethod]
        public void Inject_ModelWithoutMax_ReturnsOriginalBody()
        {
            // Arrange
            var body = """{"model":"deepseek-chat","temperature":0.7}""";

            // Act
            var result = ReasoningEffortInjector.Inject(body);

            // Assert
            Assert.AreEqual(body, result);
        }
    }
}