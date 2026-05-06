# ObsCure-Texture-Editor
ObsCure Texture Editor is a tool for editing textures from this franchise, designed for Nintendo Wii, PC, PS2, PSP, PS3 and XBOX.

## SUPPORTED GAMES

- ObsCure I
- Obscure II (Obscure: The Aftermath)
- Final Exam

## CREDITS

- [HeitorSpectre](https://github.com/HeitorSpectre)
- [Evil Trainer ](https://github.com/evil-trainer) 
- [marcos_haf — HNNEWGAMES  ](https://github.com/marcoshaf)

---

## IMAGES
<img width="1186" height="793" alt="image" src="https://github.com/user-attachments/assets/ee3508d9-6f72-4820-a638-42f98d06d76c" />

<img width="1184" height="789" alt="image" src="https://github.com/user-attachments/assets/a4a89905-65fd-46cc-8ecf-961182d2247b" />



## SUPPORTED FORMATS / PLATFORMS

### Containers
- `.dic` — Obscure / Obscure 2 texture dictionary (PC / PS2 / PSP / Wii)
- `.dip` — Obscure 1 (PC)
- `.hvi` — Obscure 1 / Obscure 2 (PS2)
- `.hvt` — Obscure 2 (Wii)
- `.hvt` — Final Exam (PC / PS3 / Xbox 360)
- `.xbr` — Obscure 1 (Xbox)

#### PC `.dic`
- R8G8B8A8 (32 bpp)
- R5G6B5 (16 bpp)
- R5G5B5A1 (16 bpp)

#### PC `.dip`
- B8G8R8A8 (32 bpp)
- B5G6R5 (16 bpp)
- B5G5R5A1 (16 bpp)

#### PS2 `.dic`
- PAL4 (4 bpp) (swizzled) (RGBA8888 palette)
- PAL8 (8 bpp) (swizzled) (RGB5551 or RGBA8888 palette)
- RGB5551 (16 bpp) (little-endian)
- RGBA8888 (32 bpp) (PS2 alpha range)

#### PSP `.dic`
- GU_PSM_T4 / PAL4 (4 bpp indexed)
- GU_PSM_T8 / PAL8 (8 bpp indexed)
- RGBA8888 CLUT / palette
- PSP swizzled indexed payload (4-byte palette padding)

#### Wii `.dic`
- I8 (8 bpp grayscale)
- IA8 (16 bpp grayscale + alpha)
- RGB5A3 (16 bpp)
- RGBA8 (32 bpp AR/GB interleaved)
- C4 / C8 (4/8 bpp paletted with TLUT)
- CMPR (4 bpp DXT1-style compression)

### Standalone Texture Files

#### PS2 `.hvi`
- PAL8 (8 bpp indexed texture with RGBA palette)

#### Wii `.hvt`
- I8
- IA8
- RGB5A3
- RGBA8
- C4
- C8
- CMPR

#### Xbox `.xbr`
- SZ_R5G6B5 (16 bpp) (Morton-order swizzled)
- SZ_A1R5G5B5 (16 bpp) (Morton-order swizzled)
- SZ_A8R8G8B8 (32 bpp) (Morton-order swizzled)

### Final Exam `.hvt`

#### PC
- BGRA (32 bpp linear)
- BGRX (32 bpp linear) (alpha forced opaque)
- DXT1 / TXD1 (BC1 compression)
- DXT3 / TXD3 (BC2 compression)
- DXT5 / TXD5 (BC3 compression)

#### PS3
- ARGB (32 bpp swizzled)
- DXT1 / TXD1 (BC1 compression)
- DXT3 / TXD3 (BC2 compression)
- DXT5 / TXD5 (BC3 compression)

#### Xbox 360
- ARGB (32 bpp tiled)
- DXT1 / TXD1 (tiled BC1 compression)
- DXT3 / TXD3 (tiled BC2 compression)
- DXT5 / TXD5 (tiled BC3 compression)
