# Monta el video: por escena (toma + audio + rótulo de bloque + subtítulos + láminas), luego une con capítulos y deja salida/ (mp4, srt, vtt, indice.html, ficha.json).
# Uso: python montar.py [--version 01] [--solo E03]   (correr con guardia-recursos.ps1 -Archivo)
import json, os, subprocess, sys, shutil, datetime

FF = "C:/Users/Luis/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/"
B = "F:/Download/TeikemVideo/solutions-proceso"
VER = sys.argv[sys.argv.index('--version') + 1] if '--version' in sys.argv else '01'
SOLO = sys.argv[sys.argv.index('--solo') + 1] if '--solo' in sys.argv else None
ESC = [('E01-productos', 'Productos e inventario'), ('E02-compras', 'Órdenes de compra'), ('E03-recibo', 'Recibo'), ('E04-venta', 'Venta con el asistente'),
       ('E05-despacho', 'Salida del almacén'), ('E06-quickbooks', 'Facturar y exportar a QuickBooks'), ('E07-analisis', 'Análisis de ventas')]
LAM = {'E06-quickbooks': [(11, 'quickbooks-iif'), (12, 'quickbooks-importar')]}   # frase (1-based) → lámina que se muestra durante esa frase
W = f"{B}/trabajo"
for d in (W, f"{B}/montaje", f"{B}/salida"): os.makedirs(d, exist_ok=True)
shutil.copy('C:/Windows/Fonts/segoeui.ttf', f"{W}/segoeui.ttf")


def run(cmd, cwd=None):
    r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode != 0:
        print(r.stderr[-1800:])
        raise SystemExit('ffmpeg falló')
    return r.stdout


def dur(p):
    return float(run([FF + 'ffprobe.exe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', p]).strip())


def srt_t(t):
    ms = int(round(t * 1000))
    return f"{ms // 3600000:02d}:{ms // 60000 % 60:02d}:{ms // 1000 % 60:02d},{ms % 1000:03d}"


inicio_global = 0.0
capitulos = []
srt_global = []
n_srt = 0
ficha = []
for i, (esc, titulo) in enumerate(ESC, 1):
    out = f"{B}/montaje/{esc}.mp4"
    li = json.load(open(f"{B}/audio/{esc}.lineas.json", encoding='utf-8-sig'))
    ini, frases = li['inicios'], li['frases']
    ssrt = []
    for k, f in enumerate(frases):
        a = ini[k]
        b = (ini[k + 1] - 0.05) if k + 1 < len(ini) else li['audioSeg']
        ssrt.append(f"{k + 1}\n{srt_t(a)} --> {srt_t(b)}\n{f}\n")
        n_srt += 1
        srt_global.append(f"{n_srt}\n{srt_t(inicio_global + a)} --> {srt_t(inicio_global + b)}\n{f}\n")
    if SOLO and not esc.startswith(SOLO):
        if not os.path.exists(out):
            raise SystemExit(f'falta {out}: corra sin --solo')
    else:
        toma = json.load(open(f"{B}/tomas/{esc}.json", encoding='utf-8'))
        off = toma['offsetSeg'] or 0
        total = li['audioSeg'] + 1.8   # termina un poco después de la voz
        open(f"{W}/{esc}.srt", 'w', encoding='utf-8').write('\n'.join(ssrt))
        open(f"{W}/{esc}.titulo.txt", 'w', encoding='utf-8').write(f"Bloque {i}  ·  {titulo}")
        cmd = [FF + 'ffmpeg.exe', '-y', '-v', 'error', '-threads', '4', '-ss', f"{off:.3f}", '-i', f"{B}/tomas/{esc}.webm", '-i', f"{B}/audio/{esc}.wav"]
        lam = LAM.get(esc, [])
        for _, nombre in lam:
            cmd += ['-loop', '1', '-t', f"{total:.2f}", '-i', f"{B}/laminas/{nombre}.png"]
        graph = "[0:v]fps=25,scale=1920:1080,format=yuv420p"
        graph += f",drawtext=fontfile=segoeui.ttf:textfile={esc}.titulo.txt:fontcolor=white:fontsize=46:box=1:boxcolor=0x0f2a4acc:boxborderw=22:x=(w-text_w)/2:y=44:enable='lt(t,2.3)'"
        graph += f",subtitles={esc}.srt:force_style='FontName=Segoe UI,FontSize=10,Outline=1.2,Shadow=0,MarginV=14,PrimaryColour=&H00FFFFFF,OutlineColour=&H00402010'[v0]"
        last = 'v0'
        for j, (nfrase, nombre) in enumerate(lam):
            a = ini[nfrase - 1] + 0.2
            b = (ini[nfrase] if nfrase < len(ini) else li['audioSeg']) - 0.1
            graph += f";[{j + 2}:v]format=yuv420p,setpts=PTS-STARTPTS[l{j}];[{last}][l{j}]overlay=0:0:enable='between(t,{a:.2f},{b:.2f})'[v{j + 1}]"
            last = f"v{j + 1}"
        graph += ";[1:a]loudnorm=I=-16:TP=-1.5:LRA=11,apad[a]"
        cmd += ['-filter_complex', graph, '-map', f"[{last}]", '-map', '[a]', '-t', f"{total:.2f}", '-c:v', 'libx264', '-crf', '18', '-preset', 'medium', '-pix_fmt', 'yuv420p',
                '-r', '25', '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-movflags', '+faststart', out]
        print('montando', esc, flush=True)
        run(cmd, cwd=W)
    d = dur(out)
    capitulos.append((inicio_global, inicio_global + d, f"Bloque {i} · {titulo}"))
    ficha.append({'escena': esc, 'duracionSeg': round(d, 2)})
    inicio_global += d

open(f"{W}/lista.txt", 'w', encoding='utf-8').write('\n'.join(f"file '{B}/montaje/{e}.mp4'" for e, _ in ESC))
meta = ';FFMETADATA1\ntitle=Advance Solutions: el proceso en Teikem\n' + ''.join(
    f"[CHAPTER]\nTIMEBASE=1/1000\nSTART={int(a * 1000)}\nEND={int(b * 1000)}\ntitle={t}\n" for a, b, t in capitulos)
open(f"{W}/capitulos.txt", 'w', encoding='utf-8').write(meta)
salida = f"{B}/salida/solutions-proceso_v{VER}.mp4"
run([FF + 'ffmpeg.exe', '-y', '-v', 'error', '-f', 'concat', '-safe', '0', '-i', 'lista.txt', '-i', 'capitulos.txt', '-map', '0', '-map_metadata', '1', '-map_chapters', '1',
     '-c', 'copy', '-movflags', '+faststart', salida], cwd=W)
open(f"{B}/salida/solutions-proceso_v{VER}.srt", 'w', encoding='utf-8').write('\n'.join(srt_global))
open(f"{B}/salida/solutions-proceso_v{VER}.vtt", 'w', encoding='utf-8').write('WEBVTT\n\n' + '\n'.join(srt_global).replace(',', '.'))
caps = ''.join(f'<li><button onclick="v.currentTime={a:.2f};v.play()">{t}<small>{int(a // 60)}:{int(a % 60):02d}</small></button></li>' for a, b, t in capitulos)
html = ('<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Advance Solutions en Teikem</title>'
        '<style>body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#0f2a4a;color:#fff;display:flex;flex-wrap:wrap;gap:20px;padding:20px;justify-content:center}'
        'video{width:min(1100px,100%);border-radius:12px;background:#000}ol{list-style:none;margin:0;padding:0;min-width:300px}'
        'button{display:flex;justify-content:space-between;width:100%;gap:12px;margin:0 0 8px;padding:14px 16px;border:0;border-radius:10px;background:#fff;color:#0f2a4a;font-size:17px;font-weight:600;cursor:pointer;text-align:left}'
        'button:hover{background:#f28c28;color:#fff}small{opacity:.7}h1{width:100%;font-size:24px;margin:0}p{width:100%;margin:0;opacity:.8}</style>'
        '<h1>Advance Solutions: el proceso en Teikem</h1><p>Toque un bloque para saltar a él, o pulse las teclas 1 a 7.</p>'
        f'<video id="v" controls src="solutions-proceso_v{VER}.mp4"><track kind="subtitles" srclang="es" label="Español" src="solutions-proceso_v{VER}.vtt" default></video><ol>{caps}</ol>'
        f'<script>const v=document.getElementById("v");addEventListener("keydown",e=>{{const n=parseInt(e.key);if(n>=1&&n<={len(capitulos)}){{document.querySelectorAll("button")[n-1].click()}}}})</script></html>')
open(f"{B}/salida/indice.html", 'w', encoding='utf-8').write(html)
tam = os.path.getsize(salida) / 1048576
json.dump({'version': VER, 'fecha': datetime.date.today().isoformat(), 'duracionSeg': round(inicio_global, 2), 'tamanoMB': round(tam, 1),
           'capitulos': [{'inicioSeg': round(a, 2), 'titulo': t} for a, _, t in capitulos], 'escenas': ficha, 'base': 'Teikem_video', 'voz': 'es-US-Chirp3-HD-Charon'},
          open(f"{B}/salida/ficha.json", 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
print('listo', salida, f"{inicio_global:.1f} s", f"{tam:.1f} MB")
