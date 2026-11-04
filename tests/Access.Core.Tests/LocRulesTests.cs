using GwentAccess;
using Xunit;

namespace Access.Core.Tests
{
    /// <summary>The translation rules behind Loc: CLDR plural forms per language, and a translation with a
    /// broken placeholder falling back to the English instead of saying nothing.</summary>
    public class LocRulesTests
    {
        [Theory]
        [InlineData("en-us", 1, 0)]
        [InlineData("en-us", 0, 1)]
        [InlineData("en-us", 2, 1)]
        [InlineData("ru-ru", 1, 0)]
        [InlineData("ru-ru", 21, 0)]
        [InlineData("ru-ru", 11, 2)]
        [InlineData("ru-ru", 3, 1)]
        [InlineData("ru-ru", 13, 2)]
        [InlineData("ru-ru", 25, 2)]
        [InlineData("pl-pl", 1, 0)]
        [InlineData("pl-pl", 21, 2)]
        [InlineData("pl-pl", 22, 1)]
        [InlineData("pl-pl", 5, 2)]
        [InlineData("cs-cz", 3, 1)]
        [InlineData("cs-cz", 5, 2)]
        [InlineData("fr-fr", 0, 0)]
        [InlineData("pt-br", 0, 0)]
        [InlineData("pt-br", 2, 1)]
        [InlineData("ja-jp", 1, 0)]
        [InlineData("zh-cn", 7, 0)]
        [InlineData(null, 1, 0)]
        public void PluralIndexFollowsCldr(string lang, long n, int expected) =>
            Assert.Equal(expected, LocRules.PluralIndex(lang, n));

        [Fact]
        public void PluralPicksTheLanguagesForm() =>
            Assert.Equal("5 карт", LocRules.Plural("{0} карта|{0} карты|{0} карт", "ru-ru", 5, "{0} card", "{0} cards", null));

        [Fact]
        public void PluralWithoutTranslationIsEnglish()
        {
            Assert.Equal("1 card", LocRules.Plural(null, "de-de", 1, "{0} card", "{0} cards", null));
            Assert.Equal("3 cards: A", LocRules.Plural(null, "de-de", 3, "{0} card: {1}", "{0} cards: {1}", new object[] { "A" }));
        }

        [Fact]
        public void PluralWithTooFewFormsUsesTheLast() =>
            Assert.Equal("5 Karten", LocRules.Plural("{0} Karte|{0} Karten", "ru-ru", 5, "{0} card", "{0} cards", null));

        [Fact]
        public void BrokenTranslationFallsBackToEnglish() =>
            Assert.Equal("You draw 2 cards", LocRules.Format("Du ziehst {5} Karten", "You draw {0} cards", new object[] { 2 }));

        [Fact]
        public void BrokenEnglishIsSaidAsWritten() =>
            Assert.Equal("Bad {9} line", LocRules.Format("Bad {9} line", "Bad {9} line", new object[] { 1 }));

        [Fact]
        public void NumbersAreInvariant() =>
            Assert.Equal("1.5", LocRules.Format("{0}", "{0}", new object[] { 1.5 }));

        [Theory]
        [InlineData("ru-ru", "ru")]
        [InlineData("pt", "pt")]
        [InlineData("", "en")]
        public void BaseDropsTheRegion(string lang, string expected) => Assert.Equal(expected, LocRules.Base(lang));
    }
}
