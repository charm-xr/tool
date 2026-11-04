from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
out = root / 'assets'
out.mkdir(exist_ok=True)
im = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
d.rounded_rectangle((4, 4, 252, 252), radius=52, fill='#17283d')
d.rounded_rectangle((49, 86, 207, 197), radius=18, fill='#00a69a')
d.rounded_rectangle((91, 54, 165, 108), radius=16, outline='#e7fffb', width=13)
d.rounded_rectangle((40, 88, 216, 126), radius=12, fill='#def9f4')
d.rounded_rectangle((115, 113, 141, 146), radius=6, fill='#17283d')
im.save(out / 'cleanbox.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.save(out / 'cleanbox.png')
