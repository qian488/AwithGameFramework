using System.Collections.Generic;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Extensions.Localization;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class LocalizationTests
    {
        private LocalizationManager manager;

        [SetUp]
        public void SetUp()
        {
            EventCenter.GetInstance().Clear();
            ServiceLocator.Clear();
            manager = new LocalizationManager { DefaultLanguage = "en" };
            manager.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            manager.Shutdown();
            EventCenter.GetInstance().Clear();
            ServiceLocator.Clear();
        }

        [Test]
        public void SuppliedDatasets_ShouldSwitchTextAndClearPreviousLanguageEntries()
        {
            manager.LoadDataset(new LocalizationDataset
            {
                LanguageCode = "en",
                Entries = new List<LocalizationEntry>
                {
                    new LocalizationEntry { Key = "hello", Value = "Hello" },
                    new LocalizationEntry { Key = "english-only", Value = "English only" }
                }
            });
            manager.LoadDataset(new LocalizationDataset
            {
                LanguageCode = "zh",
                Entries = new List<LocalizationEntry> { new LocalizationEntry { Key = "hello", Value = "你好" } }
            });

            manager.SwitchLanguage("en");
            Assert.That(manager.GetText("hello"), Is.EqualTo("Hello"));
            manager.SwitchLanguage("zh");
            Assert.That(manager.CurrentLanguage, Is.EqualTo("zh"));
            Assert.That(manager.GetText("hello"), Is.EqualTo("你好"));
            Assert.That(manager.GetText("english-only"), Is.EqualTo("english-only"));
            Assert.That(manager.GetText("missing", "Default text"), Is.EqualTo("Default text"));
        }

        [Test]
        public void MissingLanguage_ShouldFallbackAndPublishActualLanguage()
        {
            manager.LoadDataset(new LocalizationDataset
            {
                LanguageCode = "en",
                Entries = new List<LocalizationEntry> { new LocalizationEntry { Key = "hello", Value = "Hello" } }
            });
            string reportedLanguage = null;
            EventCenter.GetInstance().AddEventListener<string>(LocalizationEvents.LanguageChanged, language => reportedLanguage = language);

            manager.SwitchLanguage("unavailable");

            Assert.That(manager.GetText("hello"), Is.EqualTo("Hello"));
            Assert.That(manager.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(reportedLanguage, Is.EqualTo(manager.CurrentLanguage));
        }
    }
}
