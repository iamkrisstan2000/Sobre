import re, json, urllib.request, concurrent.futures as cf

ICONS = [
 ("Mail","Mail"),("MailInbox","Mail Inbox"),("MailUnread","Mail Unread"),("MailRead","Mail Read"),
 ("MailAlert","Mail Alert"),("MailProhibited","Mail Prohibited"),("MailAllRead","Mail All Read"),
 ("Send","Send"),("SendClock","Send Clock"),("Drafts","Drafts"),("Archive","Archive"),("Delete","Delete"),
 ("Star","Star"),("StarOff","Star Off"),("Attach","Attach"),("ArrowReply","Arrow Reply"),
 ("ArrowReplyAll","Arrow Reply All"),("ArrowForward","Arrow Forward"),("Compose","Compose"),
 ("Search","Search"),("Settings","Settings"),("Person","Person"),("People","People"),
 ("PersonAccounts","Person Accounts"),("Signature","Signature"),("DocumentSignature","Document Signature"),
 ("DarkTheme","Dark Theme"),("WeatherSunny","Weather Sunny"),("Desktop","Desktop"),("Phone","Phone"),
 ("Checkmark","Checkmark"),("CheckmarkCircle","Checkmark Circle"),("Dismiss","Dismiss"),("Add","Add"),
 ("ChevronDown","Chevron Down"),("ChevronRight","Chevron Right"),("ChevronLeft","Chevron Left"),
 ("ChevronUp","Chevron Up"),("MoreHorizontal","More Horizontal"),("Filter","Filter"),
 ("Shield","Shield"),("ShieldCheckmark","Shield Checkmark"),("LockClosed","Lock Closed"),("Key","Key"),
 ("ArrowSync","Arrow Sync"),("ArrowClockwise","Arrow Clockwise"),("Cloud","Cloud"),("Server","Server"),
 ("Flag","Flag"),("Pin","Pin"),("Folder","Folder"),("FolderOpen","Folder Open"),("Info","Info"),
 ("Warning","Warning"),("ErrorCircle","Error Circle"),("QuestionCircle","Question Circle"),
 ("Eye","Eye"),("EyeOff","Eye Off"),("Save","Save"),("Copy","Copy"),("Print","Print"),
 ("Subtract","Subtract"),("Square","Square"),("SquareMultiple","Square Multiple"),("Color","Color"),
 ("PaintBrush","Paint Brush"),("TextBold","Text Bold"),("TextItalic","Text Italic"),
 ("TextUnderline","Text Underline"),("TextBulletList","Text Bullet List"),("TextAlignLeft","Text Align Left"),
 ("Link","Link"),("Image","Image"),("Emoji","Emoji"),("ArrowDownload","Arrow Download"),
 ("ArrowUpload","Arrow Upload"),("ArrowLeft","Arrow Left"),("ArrowRight","Arrow Right"),
 ("ZoomIn","Zoom In"),("ZoomOut","Zoom Out"),("Keyboard","Keyboard"),("Clock","Clock"),("Tag","Tag"),
 ("Alert","Alert"),("Bookmark","Bookmark"),("Sparkle","Sparkle"),("ScanQRCode","Scan QR Code"),
 ("Navigation","Navigation"),("PanelLeft","Panel Left"),("ReadingMode","Reading Mode"),("Globe","Globe"),
 ("Notebook","Notebook"),("Calendar","Calendar"),("ContactCard","Contact Card"),("ArrowSort","Arrow Sort"),
 ("DocumentText","Document Text"),("BookOpen","Book Open"),("DesktopKeyboard","Desktop Keyboard"),
 ("MailCopy","Mail Copy"),("MailArrowUp","Mail Arrow Up"),("MailCheckmark","Mail Checkmark"),
 ("MailInboxArrowDown","Mail Inbox Arrow Down"),("MailList","Mail List"),("TextQuote","Text Quote"),
 ("ArrowUndo","Arrow Undo"),("ArrowRedo","Arrow Redo"),("Cut","Cut"),("Paste","Paste"),
 ("TextGrammarCheckmark","Text Grammar Checkmark"),("WidthWide","Width Wide"),("FilterDismiss","Filter Dismiss"),
 ("MailTemplate","Mail Template"),("WindowNew","Window New"),("OpenFolder","Open Folder"),
 ("Share","Share"),("ArrowExportUp","Arrow Export Up"),("SendCopy","Send Copy"),
 ("FolderMail","Folder Mail"),("StarAdd","Star Add"),("MailOff","Mail Off"),("Inprivate","Inprivate"),
]

BASE = "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets/{f}/SVG/ic_fluent_{s}_{sz}_{v}.svg"

def fetch(item):
    key, folder = item
    slug = folder.lower().replace(" ", "_").replace("-", "_")
    out = {}
    for variant in ("regular", "filled"):
        url = BASE.format(f=urllib.parse.quote(folder), s=slug, sz=24, v=variant)
        try:
            with urllib.request.urlopen(url, timeout=30) as r:
                svg = r.read().decode("utf-8")
            ds = re.findall(r'<path[^>]*\sd="([^"]+)"', svg)
            if ds: out[variant] = " ".join(ds)
        except Exception:
            pass
    return key, out

import urllib.parse
with cf.ThreadPoolExecutor(max_workers=16) as ex:
    results = list(ex.map(fetch, ICONS))

data, missing = {}, []
for key, out in results:
    if "regular" in out: data[key] = out
    else: missing.append(key)

json.dump(data, open("/home/user/sobre/tools/fluent_paths.json", "w"), indent=0)
print("fetched:", len(data), "/", len(ICONS))
print("missing:", missing)
