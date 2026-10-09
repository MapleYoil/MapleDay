"""Original, quiet acoustic piano cue. Offline SoundFont rendering only."""
from pathlib import Path
import wave
import numpy as np
import tinysoundfont

HERE=Path(__file__).resolve().parents[2]/'artifacts/Promo/source'

def render_score(duration=15):
    sr=48000
    synth=tinysoundfont.Synth(samplerate=sr,gain=-8)
    bank=synth.sfload(str(HERE/'GeneralUser-GS.sf2'))
    for channel in range(3):synth.program_select(channel,bank,0,0)
    events=[]
    def note(at,pitch,length,velocity=48,channel=0):
        at*=duration/26;length*=duration/26
        events.append((round(at*sr),True,channel,pitch,velocity))
        events.append((round((at+length)*sr),False,channel,pitch,0))
    # Low-velocity open voicings, spaced by hand. No electronic layers or chimes.
    voicings=[(0,(50,57,61,64)),(4,(47,54,57,62)),(8,(43,50,57,59)),(12,(45,52,59,61)),(16,(50,57,61,64)),(20,(43,50,55,59)),(23,(50,57,62,66))]
    for at,chord in voicings:
        note(at+.05,chord[0]-12,3.2,46,2)
        for j,pitch in enumerate(chord):note(at+.16+j*.10,pitch,3.0 if at<23 else 2.6,40+j*2,0)
        if at<20:
            note(at+2.15,chord[1],1.55,37,0)
            note(at+2.55,chord[3],1.2,39,0)
    melody=[(.85,66),(1.8,64),(3,61),(4.8,62),(5.9,61),(7,59),(8.8,59),(10,62),(11.1,64),(12.8,61),(14,64),(15.1,62),(16.8,66),(18,64),(19.1,61),(20.8,62),(22,59),(23.6,62)]
    for j,(at,pitch) in enumerate(melody):note(at,pitch,1.6,49+(j%3)*2,1)
    # Warm resolution at the 1T arrival (about 8.03s in the 15s film).
    for j,pitch in enumerate((38,50,57,62)):
        note(13.92+j*.045,pitch,2.3,48-j*2,2)
    events.sort(key=lambda e:(e[0],e[1]))
    length=int(duration*sr);mix=np.zeros((length,2),np.float32);cursor=0
    for offset,on,channel,pitch,velocity in events:
        offset=min(length,offset)
        if offset>cursor:
            mix[cursor:offset]=np.frombuffer(synth.generate_simple(offset-cursor),dtype=np.float32).reshape(-1,2)
            cursor=offset
        if on:synth.noteon(channel,pitch,velocity)
        else:synth.noteoff(channel,pitch)
    if cursor<length:mix[cursor:]=np.frombuffer(synth.generate_simple(length-cursor),dtype=np.float32).reshape(-1,2)
    # A small, warm room; echoes use the dry signal, avoiding feedback buildup.
    dry=mix.copy()
    for delay,gain in [(.034,.10),(.061,.09),(.097,.075),(.157,.06),(.223,.035),(.317,.02)]:
        d=int(delay*sr);mix[d:]+=dry[:-d,::-1]*gain
    t=np.arange(length)/sr
    swell=1+.14*np.sin(np.clip((t-4.1)/3.36,0,1)*np.pi/2)
    mix*=swell[:,None]
    mix*=np.minimum(1,t[:,None]/.45)*np.minimum(1,(duration-t[:,None])/1.8)
    peak=float(np.max(np.abs(mix)))
    if peak>.5:mix*=.5/peak
    path=HERE/'score.wav'
    with wave.open(str(path),'wb') as wav:
        wav.setnchannels(2);wav.setsampwidth(2);wav.setframerate(sr)
        wav.writeframes((np.clip(mix,-1,1)*32767).astype('<i2').tobytes())
    return path

if __name__=='__main__':print(render_score())
