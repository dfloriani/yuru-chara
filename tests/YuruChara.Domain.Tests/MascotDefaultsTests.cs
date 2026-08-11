using YuruChara.Domain.Mascots;

namespace YuruChara.Domain.Tests;

/// <summary>
/// These default values are what enforces the project's "do not invent mascot
/// data" rule: a Mascot constructed without an explicit decision must take the
/// cautious value rather than the one that claims more. Asserting them here means
/// that reordering either enum causes a test failure, instead of causing all 47
/// prefectures to report themselves as hand-verified.
/// </summary>
public class MascotDefaultsTests
{
    [Fact]
    public void UnresearchedImageLicence_DefaultsToUnknown()
    {
        var mascot = new Mascot { NameJa = "テスト" };

        Assert.Equal(ImageLicenseStatus.Unknown, mascot.ImageLicenseStatus);
        Assert.Equal(0, (int)ImageLicenseStatus.Unknown);
    }

    [Fact]
    public void UnverifiedMascot_DefaultsToAutomated()
    {
        var mascot = new Mascot { NameJa = "テスト" };

        Assert.Equal(VerificationLevel.Automated, mascot.VerificationLevel);
        Assert.Equal(0, (int)VerificationLevel.Automated);
    }

    [Fact]
    public void NewMascot_HasNoCitations_RatherThanNull()
    {
        var mascot = new Mascot { NameJa = "テスト" };

        Assert.Empty(mascot.SourceCitations);
    }
}
