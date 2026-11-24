"""Draws Hearwood's icon: a cedar-green tile with white waveform bars that rise to a peak like an evergreen.

    python tools/make_icon.py

Writes src/Hearwood/assets/Hearwood.ico (16-256 px) and Hearwood.png (256 px), and the MSIX package's logos in
packaging/Assets (Start, taskbar and Store images at 100% and 200% scale). Each size is drawn on its own at 8x and
scaled down, and the smallest sizes use fewer, thicker bars so the tray icon stays legible.
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Hearwood" / "assets"
PACKAGE_ASSETS = ROOT / "packaging" / "Assets"
TOP = (52, 168, 140)     # #34A88C
BOTTOM = (20, 84, 72)    # #145448
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]


def tile(size: int) -> Image.Image:
    scale = 8
    s = size * scale
    # The tile fills the canvas at small sizes (every pixel counts) and leaves a margin at large ones.
    margin = 0 if size <= 24 else round(s * 0.04)
    radius = round(s * (0.18 if size <= 24 else 0.22))

    gradient = Image.new("RGB", (1, s))
    for y in range(s):
        t = y / (s - 1)
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)))
    gradient = gradient.resize((s, s))

    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle((margin, margin, s - 1 - margin, s - 1 - margin), radius, fill=255)
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    image.paste(gradient, (0, 0), mask)

    heights = [0.5, 1.0, 0.5] if size <= 20 else [0.34, 0.66, 1.0, 0.66, 0.34]
    inner = s - 2 * margin
    span = inner * (0.62 if size <= 20 else 0.66)
    pitch = span / len(heights)
    bar = pitch * (0.62 if size <= 20 else 0.56)
    tallest = inner * 0.6
    left = (s - span) / 2 + (pitch - bar) / 2
    centre = s / 2
    draw = ImageDraw.Draw(image)
    for i, h in enumerate(heights):
        x = left + i * pitch
        half = max(tallest * h / 2, bar / 2)
        draw.rounded_rectangle((x, centre - half, x + bar, centre + half), bar / 2, fill=(255, 255, 255, 255))

    return image.resize((size, size), Image.LANCZOS)


def centred(width: int, height: int, icon: int) -> Image.Image:
    """The icon at `icon` px in the middle of a transparent width x height canvas, for the Start tiles."""
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    canvas.paste(tile(icon), ((width - icon) // 2, (height - icon) // 2))
    return canvas


def package_assets() -> None:
    """The images packaging/AppxManifest.xml names, with the scale and target-size qualifiers MRT picks from."""
    PACKAGE_ASSETS.mkdir(parents=True, exist_ok=True)
    for old in PACKAGE_ASSETS.glob("*.png"):
        old.unlink()
    for scale in (100, 200):
        k = scale / 100
        tile(round(50 * k)).save(PACKAGE_ASSETS / f"StoreLogo.scale-{scale}.png")
        tile(round(44 * k)).save(PACKAGE_ASSETS / f"Square44x44Logo.scale-{scale}.png")
        centred(round(150 * k), round(150 * k), round(96 * k)).save(PACKAGE_ASSETS / f"Square150x150Logo.scale-{scale}.png")
        centred(round(310 * k), round(150 * k), round(96 * k)).save(PACKAGE_ASSETS / f"Wide310x150Logo.scale-{scale}.png")
    # The taskbar and Start list take these by pixel size; "unplated" stops Windows drawing an accent square behind.
    for size in (16, 24, 32, 48, 256):
        image = tile(size)
        image.save(PACKAGE_ASSETS / f"Square44x44Logo.targetsize-{size}.png")
        image.save(PACKAGE_ASSETS / f"Square44x44Logo.targetsize-{size}_altform-unplated.png")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    images = [tile(n) for n in SIZES]
    images[-1].save(OUT / "Hearwood.png")
    images[-1].save(OUT / "Hearwood.ico", sizes=[(n, n) for n in SIZES], append_images=images[:-1])
    print(f"Wrote {OUT / 'Hearwood.ico'} and Hearwood.png")
    package_assets()
    print(f"Wrote the package logos in {PACKAGE_ASSETS}")


if __name__ == "__main__":
    main()
