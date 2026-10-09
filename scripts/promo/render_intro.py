"""Offline product film compositor. Real API data, original app assets, no desktop capture."""
from pathlib import Path
import math, json, subprocess, argparse, sys, wave
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops
from functools import lru_cache

ROOT=Path(__file__).resolve().parents[2]
HERE=ROOT/'artifacts/Promo/source'
OUT=HERE.parent
ASSET=ROOT/'src/MapleDay/Assets'
DATA=json.loads((HERE/'data.json').read_text(encoding='utf-8'))
CHARS=DATA['Characters']
W,H,FPS,DURATION=1920,1080,60,13.5
BLUE='#377DFF'; INK='#202426'; MUTED='#777C83'; CREAM='#F7F7F4'; LINE='#E5E6E4'
MASKED_NICKNAME='••••••'
RESAMPLE=Image.Resampling.LANCZOS

@lru_cache(None)
def font(size,weight=400,kind='pretendard'):
    file={'pretendard':'PretendardVariable.ttf','nanum':'NanumGothicBold.ttf','morris':'Morris9.ttf'}[kind]
    f=ImageFont.truetype(str(ASSET/'Fonts'/file),size)
    if kind=='pretendard': f.set_variation_by_axes([weight])
    return f

def txt(im,xy,text,size=20,weight=400,color=INK,anchor='la',kind='pretendard'):
    d=ImageDraw.Draw(im);d.text(xy,str(text),font=font(size,weight,kind),fill=color,anchor=anchor)

def fitted(s,width,size=20,weight=400,kind='pretendard'):
    f=font(size,weight,kind)
    if f.getlength(s)<=width:return s
    while s and f.getlength(s+'…')>width:s=s[:-1]
    return s+'…'

def rr(im,box,r=16,fill='white',outline=None,width=1):
    ImageDraw.Draw(im).rounded_rectangle(box,radius=r,fill=fill,outline=outline,width=width)

@lru_cache(None)
def asset(name):return Image.open(ASSET/name).convert('RGBA')

def paste(im,source,x,y,scale=1):
    if scale!=1:source=source.resize((max(1,round(source.width*scale)),max(1,round(source.height*scale))),RESAMPLE)
    im.alpha_composite(source,(round(x),round(y)))

@lru_cache(None)
def avatar(index,size=108):
    c=CHARS[index];a=Image.open(HERE/c['Image']).convert('RGBA')
    # Match the app's fixed Chocomarin reference coordinates exactly.
    return a.crop((89,103,197,223)).resize((size,round(size*120/108)),Image.Resampling.NEAREST)

def icon(im,name,x,y,size=19,color=INK):
    d=ImageDraw.Draw(im);w=2
    if name=='characters':
        d.ellipse((x+6,y,x+13,y+7),outline=color,width=w);d.rounded_rectangle((x+2,y+10,x+17,y+18),radius=4,outline=color,width=w)
    elif name=='scheduler':
        d.rounded_rectangle((x+1,y+2,x+18,y+18),radius=3,outline=color,width=w);d.line((x+1,y+7,x+18,y+7),fill=color,width=w)
        for xx in [6,12]:d.line((x+xx,y,x+xx,y+4),fill=color,width=w)
        for xx in [5,10,15]:d.ellipse((x+xx,y+11,x+xx+1,y+12),fill=color)
    elif name=='income':
        d.line((x,y+1,x,y+18,x+19,y+18),fill=color,width=w);d.line((x+3,y+13,x+8,y+8,x+12,y+11,x+18,y+3),fill=color,width=w)
    elif name=='level':
        d.line((x+10,y+18,x+10,y+1,x+4,y+7),fill=color,width=w);d.line((x+10,y+1,x+16,y+7),fill=color,width=w)
    elif name=='notifications':
        d.rounded_rectangle((x,y+1,x+19,y+14),radius=2,outline=color,width=w);d.line((x+10,y+15,x+10,y+18,x+15,y+18),fill=color,width=w)
    elif name=='support':
        d.ellipse((x+1,y+1,x+16,y+15),outline=color,width=w);d.line((x+4,y+14,x+2,y+19),fill=color,width=w);txt(im,(x+9,y+7),'?',12,500,color,anchor='mm')
    elif name=='settings':
        d.ellipse((x+2,y+2,x+17,y+17),outline=color,width=w);d.ellipse((x+7,y+7,x+12,y+12),outline=color,width=w)
    else:
        d.ellipse((x+1,y+1,x+10,y+10),outline=color,width=w);d.line((x+9,y+9,x+18,y+18),fill=color,width=w)

def world(im,c,x,y,size=14):
    ids={'오로라':4,'레드':5,'이노시스':6,'유니온':7,'스카니아':8,'루나':9,'제니스':10,'크로아':11,'베라':12,'엘리시움':13,'아케인':14,'노바':15,'에오스':3,'핼리오스':2}
    chosen=ASSET/'Worlds'/f"icon_{ids.get(c['World'],1)}.png"
    paste(im,Image.open(chosen).convert('RGBA').resize((size,size),RESAMPLE),x,y)
    txt(im,(x+size+6,y+7),c['World'],12,400,MUTED,anchor='lm')

def app_shell(page):
    im=Image.new('RGBA',(1200,800),CREAM);d=ImageDraw.Draw(im)
    rr(im,(0,0,1199,799),11,CREAM,LINE)
    d.line((210,50,1198,50),fill=LINE,width=1);d.line((210,50,210,799),fill=LINE,width=1)
    txt(im,(23,25),'‹',24,400,MUTED,anchor='mm');rr(im,(53,7,92,43),4,BLUE)
    for yy in [17,24,31]:d.line((64,yy,80,yy),fill='white',width=1)
    paste(im,asset('Branding/logo.png'),104,14,.09);txt(im,(133,25),'메요일 · MapleDay',13,400,anchor='lm')
    for x,s in [(1080,'−'),(1127,'□'),(1175,'×')]:txt(im,(x,25),s,16,400,MUTED,anchor='mm')
    nav=[('characters','내 캐릭터'),('scheduler','스케줄러'),('income','수익'),('level','레벨'),('support','1:1 문의 / 건의사항'),('notifications','알림')]
    for i,(tag,name) in enumerate(nav):
        y=57+i*40
        if tag==page:
            rr(im,(5,y,204,y+36),4,'#EBECEA');rr(im,(5,y+10,8,y+26),2,BLUE)
        icon(im,tag,19,y+9);txt(im,(49,y+19),name,14,400,anchor='lm')
    icon(im,'key',19,727);txt(im,(49,737),'API 키',14,400,anchor='lm')
    icon(im,'settings',19,768);txt(im,(49,778),'설정',14,400,anchor='lm')
    d.line((210,758,1198,758),fill=LINE);txt(im,(235,779),'Data based on NEXON Open API',12,400,MUTED,anchor='lm')
    return im

def char_card(i,w=272,h=300):
    c=CHARS[i];im=Image.new('RGBA',(w,h));rr(im,(0,0,w-1,h-1),20,'white',LINE)
    rr(im,(w/2-54,15,w/2+54,135),13,'#F4F3EF');paste(im,avatar(i),w/2-54,15)
    world(im,c,w/2-31,144)
    txt(im,(w/2,185),MASKED_NICKNAME,18,650,anchor='mm');txt(im,(w/2,210),c['Class'],13,400,MUTED,anchor='mm')
    txt(im,(w/2,238),f"Lv. {c['Level']} ({c['ExpRate']}%)",14,500,BLUE,anchor='mm')
    rr(im,(14,h-43,w-15,h-13),5,BLUE if i>=2 else '#CBCDCB')
    txt(im,(w/2,h-28),'스케줄러에 추가' if i>=2 else '스케줄러에 추가됨',12,500,'white',anchor='mm')
    return im

def characters_page():
    im=app_shell('characters');txt(im,(235,89),'내 캐릭터',29,650)
    txt(im,(235,133),f"계정 {DATA['AccountCount']}개 · 캐릭터 {DATA['CharacterCount']}개",14,400,MUTED)
    rr(im,(235,167,425,199),5,'white',LINE);txt(im,(248,183),'전체 서버',13,400,anchor='lm');txt(im,(452,183),f"캐릭터 {DATA['CharacterCount']}개",13,400,MUTED,anchor='lm')
    for i in range(6):paste(im,char_card(i,288,300),235+(i%3)*304,223+(i//3)*315)
    # Clip the scrollable content above the persistent footer.
    im.paste(CREAM,(211,758,1199,799));ImageDraw.Draw(im).line((210,758,1198,758),fill=LINE)
    txt(im,(235,779),'Data based on NEXON Open API',12,400,MUTED,anchor='lm')
    return im

def ui(name):return asset('Scheduler/UI/'+name+'.png')

@lru_cache(None)
def game_number(text):
    widths=json.loads((ASSET/'Scheduler/Numbers/manifest.json').read_text(encoding='utf-8'))['widths']
    width=sum(widths.get(f'{ord(c):04x}',5) for c in text)
    result=Image.new('RGBA',(max(1,width),18));x=0
    for c in text:
        code=f'{ord(c):04x}';path=ASSET/'Scheduler/Numbers'/f'number_{code}.png'
        if path.exists():paste(result,Image.open(path).convert('RGBA'),x,0)
        x+=widths.get(code,5)
    return result

def game_text(im,text,x,y,right=False):
    glyphs=game_number(str(text));paste(im,glyphs,x-glyphs.width if right else x,y-9)

def native_label(im,entry,x,y,boss=False):
    path=HERE/entry.get('Label','missing')
    if path.is_file():paste(im,Image.frombytes('RGBA',(112 if boss else 165,18),path.read_bytes(),'raw','BGRA'),x,y-9)
    else:txt(im,(x,y),fitted(entry['Name'],112 if boss else 165,13,kind='nanum'),13,400,'#2C4C69',anchor='lm',kind='nanum')

def scheduler_row(entry,boss=False):
    im=Image.new('RGBA',(352,38));paste(im,ui('main_entity_back_listBoss' if boss else 'main_entity_back_list'),0,0)
    color='#2C4C69'
    if boss:
        if entry['Icon']:paste(im,asset('Scheduler/'+entry['Icon']),17,5)
        raw=entry['Difficulty'].strip().lower()
        diff={'이지':'easy','노멀':'normal','하드':'hard','카오스':'chaos','익스트림':'extreme'}.get(raw,raw)
        if diff not in ('easy','normal','hard','chaos','extreme'):
            raise ValueError(f"Unknown boss difficulty: {entry['Difficulty']}")
        paste(im,asset('Scheduler/Difficulty/'+diff+'.png'),53,9)
        native_label(im,entry,126,18,True)
        game_text(im,f"{entry.get('PartySize',1)}인",248,18)
        if entry['Complete']:paste(im,ui('main_entity_completeBoss'),0,1)
        paste(im,ui('main_entity_btMoveCompleted_normal_0' if entry['Complete'] else 'main_entity_btMove_normal_0'),279,7)
    else:
        native_label(im,entry,12,18)
        detail=entry['Detail']
        if '무릉' in entry['Name']:detail=f"{entry['Now'] or 0} 층"
        elif '수로' in entry['Name'] or '플래그' in entry['Name']:detail=f"{entry['Now'] or 0} 점"
        elif '에픽 던전' in entry['Name']:detail=f"STAGE {entry['Now'] or 0}"
        if '익스트림' in entry['Name'] and '몬스터파크' in entry['Name']:detail=''
        game_text(im,detail if detail not in ('미완료','완료') else '',262,18,True)
        button='btMove_normal'
        if entry['Type']=='quest':button='btComplete_disabled' if entry['QuestState']=='1' else 'btComplete_normal' if entry['Now']==100 else 'btStart_normal'
        paste(im,ui('main_entity_'+button+'_0'),279,7)
        if entry['Complete']:paste(im,ui('main_entity_complete'),1,1)
    return im

def header(label,quest=False,weekly=False):
    im=Image.new('RGBA',(352,38));paste(im,ui('main_entity_back_weekly' if weekly else 'main_entity_back_quest' if quest else 'main_entity_back_contents'),0,0)
    if quest:
        paste(im,ui('main_entity_btStartAll_disabled_0'),207,7);paste(im,ui('main_entity_btCompleteAll_disabled_0'),279,7)
    if weekly:
        paste(im,ui('main_entity_weeklyLimitBase'),153,9);txt(im,(324,18),f"{CHARS[0]['WeeklyClears']} / 12",11,500,'white',anchor='rm')
    return im

def scheduler_board():
    im=ui('main_backgrnd').copy();entries=CHARS[0]['Entries']
    for x,y,name in [(22,78,'daily'),(22,344,'weekly'),(402,78,'boss')]:paste(im,ui('main_'+name+'Contents_wnd_backgrnd'),x,y)
    for x,y,tag in [(355,47,'daily'),(355,313,'weekly'),(704,47,'boss')]:paste(im,ui('main_button_setting_'+tag+'Contents_normal_0'),x,y)
    paste(im,ui('main_btEnterCrossWorld_normal_0'),735,47);paste(im,ui('main_button_close_normal_0'),763,12);paste(im,ui('main_button_help_normal_0'),10,591)
    daily=[e for e in entries if e['Section']=='Daily'];weekly=[e for e in entries if e['Section']=='Weekly'];boss=[e for e in entries if e['Section'] in ('Boss','UnregisteredBoss')]
    rows=[header('CONTENTS')]+[scheduler_row(e) for e in daily if e['Type']!='quest']+[header('QUEST',quest=True)]+[scheduler_row(e) for e in daily if e['Type']=='quest']
    weeklyrows=[header('CONTENTS')]+[scheduler_row(e) for e in weekly if e['Type']!='quest']+[header('QUEST',quest=True)]+[scheduler_row(e) for e in weekly if e['Type']=='quest']
    bossrows=[header('WEEKLY',weekly=True)]+[scheduler_row(e,True) for e in boss if e.get('Difficulty') and e.get('Cycle') in ('bossWeekly','weekly')][:12]
    for rows,x,y,h in [(rows,22,78,227),(weeklyrows,22,344,227),(bossrows,402,78,493)]:
        panel=Image.new('RGBA',(352,h))
        for i,row in enumerate(rows):paste(panel,row,0,i*38)
        paste(im,panel,x,y)
        ImageDraw.Draw(im).rounded_rectangle((x+355,y+1,x+359,y+29),2,fill='#BCC3C9',outline='#8C98A2')
    return im

def money(value,plus=False):return ('+' if plus else '')+f'{value/100000000:.1f}억'

INCOMEDAYS={}
for record in DATA['Income']:
    if record['Date'].startswith('2026-10'):
        day=int(record['Date'][-2:]);INCOMEDAYS[day]=INCOMEDAYS.get(day,0)+(record['Meso'] or 0)
SELECTED_DAY=max(INCOMEDAYS,key=INCOMEDAYS.get) if INCOMEDAYS else 2

def income_page():
    im=app_shell('income');txt(im,(235,89),'수익',29,650)
    txt(im,(235,130),'스케줄러에 등록한 캐릭터의 결정 수익',14,400,MUTED)
    for x,label,val in [(235,'이번 주',DATA['WeeklyIncome']),(547,'이번 달',sum(INCOMEDAYS.values())),(859,'누적',sum(r['Meso'] or 0 for r in DATA['Income']))]:
        rr(im,(x,165,x+290,255),15,'white',LINE);txt(im,(x+22,188),label,13,400,MUTED);txt(im,(x+22,217),money(val)+' 메소',27,650,BLUE)
    rr(im,(235,277,1150,720),18,'white',LINE);txt(im,(266,305),'2026년 10월',23,650)
    txt(im,(1116,305),'‹    ›',20,400,MUTED,anchor='ra')
    week=['일','월','화','수','목','금','토']
    for i,s in enumerate(week):txt(im,(294+i*126,355),s,13,500,MUTED,anchor='mm')
    for day in range(1,32):
        idx=day+3;col=idx%7;row=idx//7;x=247+col*126;y=377+row*62
        if day==SELECTED_DAY:rr(im,(x+2,y,x+124,y+60),9,'#EAF1FF')
        txt(im,(x+14,y+15),day,13,500,BLUE if day==SELECTED_DAY else INK)
        if day in INCOMEDAYS:txt(im,(x+14,y+41),money(INCOMEDAYS[day],True),15,650,BLUE)
    return im

def level_page():
    c=CHARS[0];im=app_shell('level');txt(im,(235,89),'레벨',29,650)
    rr(im,(235,149,1150,710),18,'white',LINE);paste(im,avatar(0,82),264,170)
    txt(im,(380,186),MASKED_NICKNAME,24,650);txt(im,(380,222),f"Lv. {c['Level']} ({c['ExpRate']}%)",16,500,BLUE)
    txt(im,(264,304),'최근 7일 일평균 획득 경험치',13,400,MUTED);txt(im,(264,336),c['Average'] or '기록 수집 중',29,650,BLUE)
    txt(im,(717,304),'예상 레벨업',13,400,MUTED);txt(im,(717,336),c['Eta'] or '계산 중',24,650)
    txt(im,(264,404),'최근 7일 보유 경험치',18,600)
    vals=c['Experience'];lo=0;hi=max(x['Exp'] for x in vals)*1.06
    chart=[(313+i*125,647-(v['Exp']/hi)*188) for i,v in enumerate(vals)]
    d=ImageDraw.Draw(im)
    for i in range(3):
        yy=451+i*98;d.line((303,yy,1102,yy),fill='#EAECF0');txt(im,(287,yy),f'{hi*(1-i/2)/1e12:.0f}조',11,400,MUTED,anchor='rm')
    d.line(chart,fill=BLUE,width=3,joint='curve')
    for i,(x,y) in enumerate(chart):d.ellipse((x-4,y-4,x+4,y+4),fill=BLUE);txt(im,(x,679),vals[i]['Date'],12,400,MUTED,anchor='mm')
    return im

def notice_card():
    im=Image.new('RGBA',(770,190));rr(im,(0,0,769,189),26,'#FFFFFF',LINE)
    paste(im,asset('Branding/logo.png'),30,24,.14);txt(im,(78,42),'메요일',17,600,anchor='lm');txt(im,(738,42),'지금',13,400,MUTED,anchor='rm')
    txt(im,(33,89),'일일 퀘스트 미완료 알림',24,650)
    name=MASKED_NICKNAME;pending=sum(1 for e in CHARS[0]['Entries'] if e['Section']=='Daily' and e['Type']=='quest' and not e['Complete'])
    txt(im,(33,134),f'{name} · 미완료 {pending}개 · 00:00 초기화',17,400,MUTED)
    return im

def notifications_page():
    im=app_shell('notifications');txt(im,(235,89),'알림',29,650)
    rr(im,(1070,83,1148,115),5,'white',LINE);txt(im,(1109,99),'설정',13,400,anchor='mm')
    paste(im,notice_card().resize((915,226),RESAMPLE),235,166)
    rr(im,(235,415,1150,703),18,'white',LINE);txt(im,(259,443),'일일 퀘스트 미완료 목록',21,600)
    for i,e in enumerate([e for e in CHARS[0]['Entries'] if e['Section']=='Daily' and e['Type']=='quest' and not e['Complete']][:3]):
        rr(im,(258,492+i*62,1127,546+i*62),10,'#F7F8F6',LINE)
        rr(im,(280,509+i*62,299,528+i*62),2,None,BLUE,2)
        txt(im,(319,519+i*62),e['Name'],16,500,anchor='lm');txt(im,(1098,519+i*62),MASKED_NICKNAME,14,400,MUTED,anchor='rm')
    return im

@lru_cache(None)
def shadowed(name):
    source=SCREENS[name];pad=78;im=Image.new('RGBA',(source.width+pad*2,source.height+pad*2));m=Image.new('RGBA',im.size)
    rr(m,(pad,pad+18,pad+source.width,pad+source.height+18),16,(30,44,63,42));m=m.filter(ImageFilter.GaussianBlur(25));im.alpha_composite(m)
    if name!='board':
        mask=Image.new('L',source.size);ImageDraw.Draw(mask).rounded_rectangle((0,0,source.width-1,source.height-1),14,fill=255)
        source=source.copy();source.putalpha(ImageChops.multiply(source.getchannel('A'),mask))
    paste(im,source,pad,pad);return im

SCREENS={'board':scheduler_board(),'income':income_page(),'level':level_page(),'notifications':notifications_page(),'notice':notice_card()}

def ease(t):t=max(0,min(1,t));return t*t*t*(t*(6*t-15)+10)
def lerp(a,b,t):return a+(b-a)*t

@lru_cache(None)
def background(mode):
    y,x=np.mgrid[0:H,0:W];r=np.sqrt(((x-W*.74)/(W*.9))**2+((y-H*.56)/H)**2)
    if mode=='dark':a=np.array([19,27,45]);b=np.array([28,49,86]);v=np.exp(-r*r*2.5)
    else:a=np.array([249,249,246]);b=np.array([235,240,250]);v=np.exp(-r*r*4)*.55
    arr=(a[None,None,:]+(b-a)[None,None,:]*v[:,:,None]).astype(np.uint8)
    return Image.fromarray(arr).convert('RGBA')

def floating(im,name,x,y,scale=1,alpha=1):
    src=shadowed(name);src=src.resize((round(src.width*scale),round(src.height*scale)),RESAMPLE)
    if alpha<1:src.putalpha(src.getchannel('A').point(lambda v:round(v*alpha)))
    paste(im,src,x-78*scale,y-78*scale)

@lru_cache(maxsize=8)
def income_plate(index,hunting=False):return Image.open(HERE/('hunting-replay-frames' if hunting else 'income-replay-frames')/f'{index:03d}.png').convert('RGBA')

@lru_cache(None)
def income_timeline(hunting=False):return json.loads((HERE/('hunting-replay-timeline.json' if hunting else 'income-replay-timeline.json')).read_text(encoding='utf-8'))

def income_hero(t,hunting=False):
    # Give the count-up, falling sprites, and the final settled heap distinct readable beats.
    phase=max(0,min(1,(t-.15)/2.15))
    clock=10*phase**1.35
    index=round(clock/10*420)
    im=income_plate(index,hunting).copy()
    total=income_timeline(hunting)[index]['Total']
    arrival=.15+2.15*.8**(1/1.35)
    q=max(0,t-arrival)
    pulse=math.sin(min(1,q/.65)*math.pi)*math.exp(-q*.7) if q>0 else 0
    rr(im,(39,449,686,601),12,'#0E1726')
    glow=Image.new('RGBA',im.size)
    ImageDraw.Draw(glow).ellipse((40,470,624,565),fill=(68,154,255,round(24+pulse*55)))
    im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(30)))
    amount=f'{total/1e12:.2f}조' if total>=1e12 else f'{total/1e8:.2f}억'
    size=min(119,590/max(1,font(119,740).getlength(amount))*119)
    txt(im,(55,525),amount,round(size*(1+pulse*.075)),740,'#81BAFF' if total>=1e12 else '#F1F5FC',anchor='lm')
    # A very slow push-in keeps the entire ledger visible and settles on the final result.
    scale=1+.009*ease(t/3)
    if scale>1:
        im=im.resize((round(W*scale),round(H*scale)),RESAMPLE)
        im=im.crop(((im.width-W)//2,(im.height-H)//2,(im.width+W)//2,(im.height+H)//2))
    return im

def type_block(im,x,y,lines,t,dark=False,size=76):
    color='#FFFFFF' if dark else INK
    for i,line in enumerate(lines):
        q=ease((t-.10-i*.1)/.90)
        if q<=0:continue
        tile=Image.new('RGBA',(760,size+30));txt(tile,(0,5),line,size,680,color)
        tile.putalpha(tile.getchannel('A').point(lambda v:round(v*q)))
        paste(im,tile,x,y+i*(size+14)+(1-q)*30)

def small_label(im,text,x,y,t,dark=False):
    q=ease((t-.02)/.65);tile=Image.new('RGBA',(700,45));txt(tile,(0,0),text,18,550,'#91B2FA' if dark else BLUE)
    tile.putalpha(tile.getchannel('A').point(lambda v:round(v*q)));paste(im,tile,x,y+(1-q)*14)

def scene(index,t):
    dark=index in (3,4);im=background('dark' if dark else 'light').copy()
    if index==0:
        q=ease(t/1.3);logo=asset('Branding/logo.png').resize((round(lerp(282,228,q)),)*2,RESAMPLE)
        paste(im,logo,960-logo.width/2,190+lerp(35,0,q))
        type_block(im,0,490,[],t)
        for i,line in enumerate(['메이플의 하루를.','메요일로.']):
            p=ease((t-.35-i*.18)/1.0);tile=Image.new('RGBA',(W,120));txt(tile,(W/2,53),line,86,720,anchor='mm')
            tile.putalpha(tile.getchannel('A').point(lambda a:round(a*p)));paste(im,tile,0,492+i*110+(1-p)*28)
        if t>1.2:txt(im,(960,807),'MapleDay',24,450,MUTED,anchor='mm')
    elif index==1:
        small_label(im,'MY CHARACTERS',140,264,t)
        type_block(im,140,325,['내 캐릭터,','한눈에.'],t,size=74)
        q=ease((t-.08)/1.2);floating(im,'characters',lerp(810,730,q),lerp(226,178,q),lerp(.88,.93,q),q)
        # A character crop breaks out of the window for a short macro emphasis.
        a=ease((t-1.1)/.85);tile=char_card(0,288,300)
        if a>0:
            temp=Image.new('RGBA',(310,332));rr(temp,(12,16,300,316),20,(28,49,80,30));temp=temp.filter(ImageFilter.GaussianBlur(14));paste(temp,tile,11,7)
            temp=temp.resize((387,415),RESAMPLE);temp.putalpha(temp.getchannel('A').point(lambda v:round(v*a)));paste(im,temp,477+lerp(25,0,a),572+lerp(55,0,a))
    elif index==2:
        small_label(im,'MAPLE SCHEDULER',140,277,t)
        type_block(im,140,336,['오늘 할 일.','놓치지 않게.'],t,size=70)
        q=ease((t-.08)/1.15);scale=lerp(.96,1.13,q)+.012*math.sin(t*.65)
        floating(im,'board',lerp(945,870,q),lerp(173,134,q),scale,q)
        # The weekly progress ring is the same 12-clear representation as app.
        p=ease((t-.4)/1.0);cx,cy=323,715;d=ImageDraw.Draw(im);d.arc((cx-66,cy-66,cx+66,cy+66),-90,270,fill='#DCE3ED',width=4)
        d.arc((cx-66,cy-66,cx+66,cy+66),-90,-90+360*min(12,CHARS[0]['WeeklyClears'])/12*p,fill=BLUE,width=4)
        paste(im,avatar(0,72),cx-36,cy-49);txt(im,(cx,cy+97),f"{min(12,CHARS[0]['WeeklyClears'])}/12",23,600,BLUE,anchor='mm')
    elif index==3:
        im=income_hero(t)
    elif index==4:
        small_label(im,'EXPERIENCE HISTORY',140,241,t,True)
        type_block(im,140,299,['성장은,','선명하게.'],t,True,size=74)
        q=ease((t-.08)/1.05);floating(im,'level',lerp(800,730,q),lerp(205,174,q),.94,q)
        # A separately composed actual-data line is revealed continuously.
        vals=CHARS[0]['Experience'];p=ease((t-.8)/1.6);x0,y0,cw,ch=141,697,407,148
        d=ImageDraw.Draw(im)
        lo=min(v['Exp'] for v in vals);hi=max(v['Exp'] for v in vals)
        pts=[(x0+i*cw/(len(vals)-1),y0+ch-(v['Exp']-lo)/(hi-lo)*ch) for i,v in enumerate(vals)]
        total=p*(len(pts)-1);n=int(total);partial=pts[:n+1]
        if n<len(pts)-1:partial.append((lerp(pts[n][0],pts[n+1][0],total-n),lerp(pts[n][1],pts[n+1][1],total-n)))
        if len(partial)>1:d.line(partial,fill='#8AB3FF',width=4,joint='curve')
        for x,y in pts[:n+1]:d.ellipse((x-4,y-4,x+4,y+4),fill='#B0C9FF')
        txt(im,(x0,y0+ch+29),'최근 7일 보유 경험치',17,400,'#A4B4D1')
    elif index==5:
        small_label(im,'SMART REMINDERS',140,315,t)
        type_block(im,140,374,['잊기 전에,','알려줘요.'],t,size=74)
        q=ease(t/.9);floating(im,'notifications',lerp(850,785,q),lerp(198,168,q),.85,q)
        p=ease((t-.55)/.8);floating(im,'notice',lerp(968,863,p),lerp(804,758,p),1.04,p)
    elif index==7:
        return income_hero(t,True)
    else:
        q=ease(t/1.0);logo=asset('Branding/logo.png').resize((220,220),RESAMPLE);paste(im,logo,850,181+(1-q)*28)
        txt(im,(960,487),'메요일',116,760,anchor='mm')
        txt(im,(960,599),'당신의 메이플에, 한 번 더 여유.',37,430,MUTED,anchor='mm')
        rr(im,(794,705,1126,768),31,BLUE);txt(im,(960,736),'Windows에서 만나보세요',22,600,'white',anchor='mm')
        txt(im,(960,823),'github.com/MapleYoil/MapleDay',22,450,MUTED,anchor='mm')
        txt(im,(960,1007),'MapleYoil  ·  Data based on NEXON Open API',14,400,'#999FA5',anchor='mm')
    return im

# Open with the falling loot and income count-up, followed by a brief scheduler.
SCENES=[3,7,2,4,5,6]
STARTS=[0,3,6,7.5,9.5,11.5]
SPEEDS=[1,1,2.3,1.4,1.4,1.6]
def frame(t):
    i=max(index for index,start in enumerate(STARTS) if t>=start)
    local=t-STARTS[i];im=scene(SCENES[i],local*SPEEDS[i])
    if i>0 and local<.38:
        previous=scene(SCENES[i-1],(t-STARTS[i-1])*SPEEDS[i-1]);p=ease(local/.38)
        # Spatial continuity plus subtle optical defocus during transitions.
        if .08<p<.92:
            previous=previous.filter(ImageFilter.GaussianBlur(math.sin(p*math.pi)*1.8))
        im=Image.blend(previous,im,p)
    return im.convert('RGB')

def storyboard():
    times=[.85,2.7,3.85,5.7,6.9,9,11,13.2]
    sheet=Image.new('RGB',(1440,4*450),'#ECEDE9')
    for idx,t in enumerate(times):
        shot=frame(t).resize((720,405),RESAMPLE);sheet.paste(shot,((idx%2)*720,(idx//2)*450))
        d=ImageDraw.Draw(sheet);d.text(((idx%2)*720+18,(idx//2)*450+415),f'{t:04.1f}s / {DURATION}s',font=font(18),fill=INK)
        frame(t).save(OUT/f'frame-{idx+1}.jpg',quality=95)
    sheet.save(OUT/'storyboard.jpg',quality=95)
    for name,im in SCREENS.items():im.save(HERE/f'ui-{name}.png')
    print('Storyboard ready.',flush=True)

def render():
    dest=OUT/f'MapleDay-Intro-{DURATION}s-1080p60.mp4'
    cmd=['ffmpeg','-hide_banner','-loglevel','warning','-y','-f','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-r',str(FPS),'-i','pipe:0','-an','-c:v','libx264','-preset','medium','-crf','16','-pix_fmt','yuv420p','-profile:v','high','-level:v','4.2','-movflags','+faststart','-t',str(DURATION),str(dest)]
    p=subprocess.Popen(cmd,stdin=subprocess.PIPE)
    try:
        for number in range(round(FPS*DURATION)):
            p.stdin.write(frame(number/FPS).tobytes())
            if number%120==0:print(f'Render: {number/FPS:.0f}/{DURATION}s',flush=True)
        p.stdin.close();code=p.wait()
        if code:raise RuntimeError('Video encoder failed.')
    except BaseException:
        p.kill();raise
    print(str(dest),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--render',action='store_true');args=parser.parse_args()
    storyboard()
    if args.render:render()
