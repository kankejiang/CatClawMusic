using CatClawMusic.Core.Services;

namespace CatClawMusic.Core.Tests;

/// <summary>
/// Unit tests for MusicUtility.SplitArtistNames method.
/// Validates the multi-value artist name splitting with separator handling
/// and band name exemption logic.
/// </summary>
public class SplitArtistNamesTests
{
    // =========================================================================
    // PRD-specified acceptance cases
    // =========================================================================

    [Fact]
    public void Split_SlashSeparated_TwoChineseArtists_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("周杰伦/林俊杰");
        Assert.Equal(["周杰伦", "林俊杰"], result);
    }

    [Fact]
    public void Split_SemicolonSeparated_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("Adele;Ed Sheeran");
        Assert.Equal(["Adele", "Ed Sheeran"], result);
    }

    [Fact]
    public void Split_ChineseCommaSeparated_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("张学友、刘德华");
        Assert.Equal(["张学友", "刘德华"], result);
    }

    [Fact]
    public void Split_KnownBand_ACDC_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("AC/DC");
        Assert.Equal(["AC/DC"], result);
    }

    [Fact]
    public void Split_KnownBand_EarthWindFire_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Earth, Wind & Fire");
        Assert.Equal(["Earth, Wind & Fire"], result);
    }

    [Fact]
    public void Split_SingleArtist_ReturnsSingle()
    {
        var result = MusicUtility.SplitArtistNames("周杰伦");
        Assert.Equal(["周杰伦"], result);
    }

    [Fact]
    public void Split_Null_ReturnsEmpty()
    {
        var result = MusicUtility.SplitArtistNames(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Split_EmptyString_ReturnsEmpty()
    {
        var result = MusicUtility.SplitArtistNames("");
        Assert.Empty(result);
    }

    [Fact]
    public void Split_WhitespaceOnly_ReturnsEmpty()
    {
        var result = MusicUtility.SplitArtistNames("   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Split_FeatMarker_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("A feat. B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_FtMarker_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("A ft. B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_KnownBand_SimonAndGarfunkel_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Simon & Garfunkel");
        Assert.Equal(["Simon & Garfunkel"], result);
    }

    [Fact]
    public void Split_UnknownArtist_ReturnsAsIs()
    {
        var result = MusicUtility.SplitArtistNames("未知艺术家");
        Assert.Equal(["未知艺术家"], result);
    }

    // =========================================================================
    // KnownBandNames: case-insensitive protection
    // =========================================================================

    [Theory]
    [InlineData("ac/dc")]
    [InlineData("AC/DC")]
    [InlineData("Ac/Dc")]
    public void Split_KnownBand_CaseInsensitive_NotSplit(string input)
    {
        var result = MusicUtility.SplitArtistNames(input);
        Assert.Equal([input], result);
    }

    [Fact]
    public void Split_KnownBand_EarthWindAndFire_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Earth, Wind and Fire");
        Assert.Equal(["Earth, Wind and Fire"], result);
    }

    [Fact]
    public void Split_KnownBand_FleetwoodMac_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Fleetwood Mac");
        Assert.Equal(["Fleetwood Mac"], result);
    }

    [Fact]
    public void Split_KnownBand_GunsNRoses_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Guns N' Roses");
        Assert.Equal(["Guns N' Roses"], result);
    }

    [Fact]
    public void Split_KnownBand_MumfordAndSons_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Mumford & Sons");
        Assert.Equal(["Mumford & Sons"], result);
    }

    [Fact]
    public void Split_KnownBand_FlorencePlusMachine_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Florence + The Machine");
        Assert.Equal(["Florence + The Machine"], result);
    }

    // =========================================================================
    // Slash "/" splitting rules
    // =========================================================================

    [Fact]
    public void Split_SlashWithSpaces_SplitsRegardlessOfLength()
    {
        // " / " is a deliberate separator, unlike bare "/"
        var result = MusicUtility.SplitArtistNames("A / B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_BareSlash_BothPartsThreeOrMore_Splits()
    {
        var result = MusicUtility.SplitArtistNames("ABC/DEF");
        Assert.Equal(["ABC", "DEF"], result);
    }

    [Fact]
    public void Split_BareSlash_ShortPart_NotSplit()
    {
        // "AB" is only 2 chars → protection kicks in
        var result = MusicUtility.SplitArtistNames("AB/CD");
        Assert.Equal(["AB/CD"], result);
    }

    [Fact]
    public void Split_BareSlash_SingleChar_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("A/C");
        Assert.Equal(["A/C"], result);
    }

    [Fact]
    public void Split_BareSlash_OneSideTwoChar_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("AB/XYZ");
        Assert.Equal(["AB/XYZ"], result);
    }

    // =========================================================================
    // Semicolon rules
    // =========================================================================

    [Fact]
    public void Split_ChineseSemicolon_Splits()
    {
        var result = MusicUtility.SplitArtistNames("A；B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_MultipleSemicolons_SplitsAll()
    {
        var result = MusicUtility.SplitArtistNames("A;B;C");
        Assert.Equal(["A", "B", "C"], result);
    }

    // =========================================================================
    // Comma ", " splitting rules
    // =========================================================================

    [Fact]
    public void Split_CommaSpace_Splits()
    {
        var result = MusicUtility.SplitArtistNames("Artist1, Artist2");
        Assert.Equal(["Artist1", "Artist2"], result);
    }

    [Fact]
    public void Split_CommaWithoutSpace_NotSplit()
    {
        // Only ", " (with trailing space) is treated as separator
        var result = MusicUtility.SplitArtistNames("Artist1,Artist2");
        Assert.Equal(["Artist1,Artist2"], result);
    }

    [Fact]
    public void Split_CommaSpace_KnownBand_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Crosby, Stills & Nash");
        Assert.Equal(["Crosby, Stills & Nash"], result);
    }

    [Fact]
    public void Split_CommaSpace_KnownBand_CrosbyStillsNashYoung_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Crosby, Stills, Nash & Young");
        Assert.Equal(["Crosby, Stills, Nash & Young"], result);
    }

    // =========================================================================
    // Ampersand " & " splitting rules
    // =========================================================================

    [Fact]
    public void Split_AmpersandSpace_AllPartsThreeOrMore_Splits()
    {
        var result = MusicUtility.SplitArtistNames("Artist1 & Artist2");
        Assert.Equal(["Artist1", "Artist2"], result);
    }

    [Fact]
    public void Split_AmpersandSpace_ShortPart_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("A & B");
        Assert.Equal(["A & B"], result);
    }

    [Fact]
    public void Split_AmpersandSpace_KnownBand_NotSplit()
    {
        // "Mumford & Sons" is a known band → not split
        var result = MusicUtility.SplitArtistNames("Mumford & Sons");
        Assert.Equal(["Mumford & Sons"], result);
    }

    [Fact]
    public void Split_AmpersandNoSpace_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Artist1&Artist2");
        Assert.Equal(["Artist1&Artist2"], result);
    }

    // =========================================================================
    // Feat/Ft marker rules
    // =========================================================================

    [Fact]
    public void Split_FeatCaseInsensitive_Upper_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("A FEAT. B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_FeatCaseInsensitive_Title_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("A Feat. B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_FeatNoSpaces_NotSplit()
    {
        // "feat." without surrounding spaces is not a separator
        var result = MusicUtility.SplitArtistNames("Afeat.B");
        Assert.Equal(["Afeat.B"], result);
    }

    [Fact]
    public void Split_FtCaseInsensitive_Upper_ReturnsBoth()
    {
        var result = MusicUtility.SplitArtistNames("A FT. B");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_FeatAtStart_NotSplit()
    {
        // " feat. " at position 0 should not split
        var result = MusicUtility.SplitArtistNames(" feat. B");
        Assert.Equal([" feat. B"], result);
    }

    [Fact]
    public void Split_FeatAtEnd_NotSplit()
    {
        // " feat. " at the end doesn't leave a featured part
        var result = MusicUtility.SplitArtistNames("A feat. ");
        Assert.Equal(["A feat. "], result);
    }

    // =========================================================================
    // Recursive mixed separator handling
    // =========================================================================

    [Fact]
    public void Split_MixedSeparators_ChineseCommaAndFeat_Recursive()
    {
        // "、" splits first, then " feat. " splits one of the parts
        var result = MusicUtility.SplitArtistNames("周杰伦、A feat. B");
        Assert.Equal(["周杰伦", "A", "B"], result);
    }

    [Fact]
    public void Split_MixedSeparators_SemicolonAndSlash_Recursive()
    {
        var result = MusicUtility.SplitArtistNames("A;B/C");
        // ";" splits first → ["A", "B/C"], then "B/C" → bare slash check:
        // B len=1, C len=1 → not all >= 3 → not split
        Assert.Equal(["A", "B/C"], result);
    }

    [Fact]
    public void Split_MixedSeparators_SemicolonAndLongSlash_Recursive()
    {
        var result = MusicUtility.SplitArtistNames("Adele;Ed Sheeran/Michael");
        // ";" splits first → ["Adele", "Ed Sheeran/Michael"]
        // "Ed Sheeran/Michael" → bare slash: "Ed Sheeran" len=10, "Michael" len=7 → both >= 3 → split
        Assert.Equal(["Adele", "Ed Sheeran", "Michael"], result);
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public void Split_SingleCharacter_ReturnsAsIs()
    {
        var result = MusicUtility.SplitArtistNames("A");
        Assert.Equal(["A"], result);
    }

    [Fact]
    public void Split_EmptyAfterTrim_ReturnsAsIs()
    {
        // Single space → IsNullOrWhiteSpace=true → empty
        var result = MusicUtility.SplitArtistNames(" ");
        Assert.Empty(result);
    }

    [Fact]
    public void Split_MultipleSpacesAsArtist_ReturnsAsIs()
    {
        // Not empty/whitespace after trim, length > 1
        // This shouldn't happen in practice, but test robustness
        var result = MusicUtility.SplitArtistNames("  A  ");
        Assert.Equal(["A"], result);
    }

    [Fact]
    public void Split_ChineseSemicolonWithSpaces_SplitsAndTrims()
    {
        var result = MusicUtility.SplitArtistNames(" A ； B ");
        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Split_CommaMultipleArtists_ReturnsAll()
    {
        var result = MusicUtility.SplitArtistNames("A, B, C");
        Assert.Equal(["A", "B", "C"], result);
    }

    [Fact]
    public void Split_KnownBand_EmersonLakePalmer_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("Emerson, Lake & Palmer");
        Assert.Equal(["Emerson, Lake & Palmer"], result);
    }

    [Fact]
    public void Split_KnownBand_TwentyOnePilots_NotSplit()
    {
        // Has a space but no separators → returns as-is anyway
        var result = MusicUtility.SplitArtistNames("Twenty One Pilots");
        Assert.Equal(["Twenty One Pilots"], result);
    }

    [Fact]
    public void Split_KnownBand_WhiteStripes_NotSplit()
    {
        var result = MusicUtility.SplitArtistNames("The White Stripes");
        Assert.Equal(["The White Stripes"], result);
    }

    [Fact]
    public void Split_MultiArtistChineseComma_ThreeArtists()
    {
        var result = MusicUtility.SplitArtistNames("张学友、刘德华、周杰伦");
        Assert.Equal(["张学友", "刘德华", "周杰伦"], result);
    }

    [Fact]
    public void Split_FeatComplex_WithParentheses_KeepAsString()
    {
        // "A feat. B (Remix)" → " feat. " splits into "A" and "B (Remix)"
        var result = MusicUtility.SplitArtistNames("A feat. B (Remix)");
        Assert.Equal(["A", "B (Remix)"], result);
    }

    // =========================================================================
    // Code review: KnownBandNames completeness check
    // =========================================================================

    [Fact]
    public void KnownBandNames_Contains_EarthWindFire_WithCommaSpace()
    {
        // The key test: "Earth, Wind & Fire" must be in KnownBandNames
        // This is verified by the test above that it doesn't split.
        // Re-testing explicitly to ensure the comma-space variant is protected.
        var result = MusicUtility.SplitArtistNames("Earth, Wind & Fire");
        Assert.Single(result);
        Assert.Equal("Earth, Wind & Fire", result[0]);
    }

    [Theory]
    [InlineData("AC/DC")]
    [InlineData("Earth, Wind & Fire")]
    [InlineData("Earth, Wind and Fire")]
    [InlineData("Simon & Garfunkel")]
    [InlineData("Crosby, Stills, Nash & Young")]
    [InlineData("Crosby, Stills & Nash")]
    [InlineData("Emerson, Lake & Palmer")]
    [InlineData("Mumford & Sons")]
    [InlineData("Guns N' Roses")]
    [InlineData("Fleetwood Mac")]
    [InlineData("The White Stripes")]
    [InlineData("Twenty One Pilots")]
    [InlineData("Stone Temple Pilots")]
    [InlineData("Stone Sour")]
    [InlineData("Florence + The Machine")]
    [InlineData("Death Cab for Cutie")]
    [InlineData("Panic! At The Disco")]
    [InlineData("Tears for Fears")]
    [InlineData("Echo & The Bunnymen")]
    public void KnownBandNames_AllBands_NotSplit(string bandName)
    {
        var result = MusicUtility.SplitArtistNames(bandName);
        Assert.Single(result);
        Assert.Equal(bandName, result[0]);
    }
}
