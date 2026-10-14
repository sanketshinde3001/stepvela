"""Generate StepVela's original vector mark, app icon and installer artwork."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
root=Path(__file__).resolve().parents[1]
assets=root/'assets'
assets.mkdir(exist_ok=True)
svg='''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96"><rect width="96" height="96" rx="24" fill="#111821"/><path d="M22 64h18l20-30h14" fill="none" stroke="#68e0ba" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/><circle cx="22" cy="64" r="7" fill="#fff"/><circle cx="60" cy="34" r="7" fill="#68e0ba"/><circle cx="74" cy="34" r="7" fill="#fff"/></svg>'''
(assets/'logo.svg').write_text(svg,encoding='utf-8')
im=Image.new('RGBA',(256,256),(0,0,0,0));d=ImageDraw.Draw(im)
d.rounded_rectangle((0,0,255,255),radius=62,fill='#111821');d.line([(58,170),(106,170),(160,90),(197,90)],fill='#68e0ba',width=18,joint='curve')
for x,y,color in [(58,170,'white'),(160,90,'#68e0ba'),(197,90,'white')]:d.ellipse((x-18,y-18,x+18,y+18),fill=color)
im.save(assets/'stepvela.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.save(assets/'logo.png')
font=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',19)
banner=Image.new('RGB',(493,58),'#111821');banner.paste(im.resize((40,40)),(14,9),im.resize((40,40)))
ImageDraw.Draw(banner).text((67,17),'StepVela',font=font,fill='white');banner.save(assets/'installer-banner.bmp')
dialog=Image.new('RGB',(493,312),'white');draw=ImageDraw.Draw(dialog);draw.rectangle((0,0,164,312),fill='#111821');icon=im.resize((104,104));dialog.paste(icon,(30,64),icon)
draw.text((32,183),'StepVela',font=font,fill='white');dialog.save(assets/'installer-dialog.bmp')
print('Generated original logo, icon, and installer artwork.')
