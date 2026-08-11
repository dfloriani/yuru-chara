namespace YuruChara.Domain.Prefectures;

/// <summary>
/// The eight conventional regions of Japan. The identifiers here are ASCII. The
/// display forms that use macrons (Hokkaidō, Tōhoku, Chūbu, Chūgoku, Kyūshū) are
/// applied when the value is formatted for output, rather than being used as the
/// identifiers themselves, so that no other code has to handle non-ASCII
/// identifiers.
/// </summary>
public enum Region
{
    Hokkaido = 1,
    Tohoku,
    Kanto,
    Chubu,
    Kansai,
    Chugoku,
    Shikoku,
    Kyushu
}
