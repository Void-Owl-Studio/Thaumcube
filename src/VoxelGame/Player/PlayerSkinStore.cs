using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VoxelGame.World.Blocks;

namespace VoxelGame.Player;

public static class PlayerSkinStore
{
    public static string AppliedSkinPath => Path.Combine(Directory.GetCurrentDirectory(), "user", "player_skin.png");
    public static string BodyTypePath => Path.Combine(Directory.GetCurrentDirectory(), "user", "player_body.txt");

    public static bool HasAppliedSkin => File.Exists(AppliedSkinPath);

    public static PlayerBodyType LoadBodyType()
    {
        try
        {
            if (File.Exists(BodyTypePath) &&
                Enum.TryParse<PlayerBodyType>(File.ReadAllText(BodyTypePath).Trim(), ignoreCase: true, out var bodyType))
            {
                return bodyType;
            }
        }
        catch (Exception)
        {
        }

        return PlayerBodyType.Normal;
    }

    public static void SaveBodyType(PlayerBodyType bodyType)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BodyTypePath)!);
        File.WriteAllText(BodyTypePath, bodyType.ToString());
    }

    public static Image<Rgba32>? LoadAppliedSkin()
    {
        if (!HasAppliedSkin)
        {
            return null;
        }

        try
        {
            var image = Image.Load<Rgba32>(AppliedSkinPath);
            NormalizeSkin(image);
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool TryApplySkin(string sourcePath, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            error = "SKIN FILE WAS NOT FOUND";
            return false;
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            error = "SELECT A PNG SKIN";
            return false;
        }

        try
        {
            using var image = Image.Load<Rgba32>(sourcePath);
            NormalizeSkin(image);
            Directory.CreateDirectory(Path.GetDirectoryName(AppliedSkinPath)!);
            image.SaveAsPng(AppliedSkinPath);
            return true;
        }
        catch (Exception)
        {
            error = "FAILED TO LOAD SKIN PNG";
            return false;
        }
    }

    private static void NormalizeSkin(Image<Rgba32> image)
    {
        if (image.Width == BlockTextureAtlas.PlayerSkinWidth && image.Height == BlockTextureAtlas.PlayerSkinHeight)
        {
            return;
        }

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(BlockTextureAtlas.PlayerSkinWidth, BlockTextureAtlas.PlayerSkinHeight),
            Sampler = KnownResamplers.NearestNeighbor
        }));
    }
}
