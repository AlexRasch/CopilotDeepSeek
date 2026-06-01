namespace CopilotDeepSeekTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using CopilotDeepSeek;

[TestClass]
public class SecurityHelperTests
{
    [TestMethod]
    public void Encrypt_ShouldReturnEncryptedString_NotEqualToInput()
    {
        // Arrange
        string input = "api-key";

        // Act
        string encrypted = SecurityHelper.Encrypt(input);

        // Assert
        Assert.AreNotEqual(input, encrypted);
    }

    [TestMethod]
    public void Decrypt_ShouldReturnOriginalValue_AfterEncrypt()
    {
        // Arrange
        string input = "api-key";

        // Act
        string encrypted = SecurityHelper.Encrypt(input);
        string decrypted = SecurityHelper.Decrypt(encrypted);

        // Assert
        Assert.AreEqual(input, decrypted);
    }
}