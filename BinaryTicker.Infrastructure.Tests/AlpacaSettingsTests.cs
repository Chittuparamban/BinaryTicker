/*
using System;
using NUnit.Framework;
using BinaryTicker.Infrastructure;

namespace BinaryTicker.Infrastructure.Tests
{
    [TestFixture]
    public class AlpacaSettingsTests
    {
        [Test]
        public void ValidSettings_DoNotThrow()
        {
            var settings = new AlpacaSettings(
                "testKey",
                "testSecret",
                "iex",
                "raw",
                true,
                TimeSpan.FromSeconds(30)                
            );

            Assert.DoesNotThrow(
                () => settings.ValidatePaperEnvironment());
        }

        [Test]
        public void EmptyKey_ThrowsArgumentException()
        {
            var settings = new AlpacaSettings(
                "",
                "testSecret"
            );

            Assert.Throws<ArgumentException>(
                () => settings.ValidatePaperEnvironment());
        }

        [Test]
        public void LiveTrading_ThrowsInvalidOperationException()
        {
            var settings = new AlpacaSettings(
                "testKey",
                "testSecret",
                isPaperTrading: false
            );

            Assert.Throws<InvalidOperationException>(
                () => settings.ValidatePaperEnvironment());
        }
    }
}
**/