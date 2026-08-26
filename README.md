# Steam Haptics

Exploratory repository for audio streaming to Steam Controller haptics

## Resources

### Conversion with FFMPEG

```bash
# 16 bit pcm
ffmpeg -i <input> -f s16le -c:a pcm_s16le -ar 8000 -ac 2 <output.pcm>
# 8 bit mulaw
ffmpeg -i <input> -ar 8000 -ac 1 -f mulaw <output.ulaw>
```

### Steam Controller Protocol

```
-- 8-bit streaming ---
if we are streaming 8-bit audio, the report is structured as follows:
report[0] = 0x88
report[1] = 31
[31 bytes of left channel samples]
[31 bytes of right channel samples]

-- 16-bit streaming ---
if we are streaming 16-bit audio, the report is structured as follows:
report[0] = 0x88
report[1] = 30
[30 bytes of left channel samples] -> [1 byte padding]
[30 bytes of right channel samples] -> [1 byte padding]
and each sample is s16le (signed 16-bit little-endian)
```