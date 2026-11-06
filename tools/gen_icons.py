import json
data = json.load(open('/home/user/sobre/tools/fluent_paths.json'))
HEAD = '''<!--
    Fluent System Icons — generated from microsoft/fluentui-system-icons (MIT).
    Geometry is stored in a 24 x 24 design grid; IconPresenter scales it to any size.
    Regenerate with tools/gen_icons.py — do not hand-edit.
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
'''
lines = [HEAD]
def emit(key, d):
    d = d.replace('&','&amp;').replace('<','&lt;').replace('>','&gt;')
    return f'    <PathGeometry x:Key="Icon.{key}" Figures="{d}" FillRule="EvenOdd" />\n'
for key in sorted(data):
    v = data[key]
    if 'regular' in v: lines.append(emit(key, v['regular']))
    if 'filled' in v:  lines.append(emit(key + '.Filled', v['filled']))
lines.append('</ResourceDictionary>\n')
open('/home/user/sobre/src/Sobre/Themes/Icons.xaml','w').write(''.join(lines))
print('icons:', len(data), 'geometries:', sum(1 for _ in open('/home/user/sobre/src/Sobre/Themes/Icons.xaml') if 'PathGeometry x:Key' in _))
