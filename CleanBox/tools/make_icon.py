from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
out = root / 'assets'

# Keep the approved artwork as the source of truth. This only exports the
# Windows icon container; it never redraws or overwrites the source PNG.
sizes = [(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)]
with Image.open(out / 'cleanbox.png') as source:
    if source.width != source.height or source.width < 256:
        raise ValueError('The source icon must be square and at least 256 pixels.')
    with source.convert('RGBA') as icon:
        # DIB frames also render correctly in .NET Framework's Icon.ToBitmap,
        # which can misread PNG-compressed frames in a multi-size ICO.
        icon.save(out / 'cleanbox.ico', format='ICO', sizes=sizes,
                  bitmap_format='bmp')

print('Exported assets/cleanbox.ico with 10 sizes (16-256 px).')
