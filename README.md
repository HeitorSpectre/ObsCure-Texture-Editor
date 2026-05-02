# ObsCure-Texture-Editor
ObsCure Texture Editor is a tool for editing textures from this franchise, designed for Nintendo Wii, PC, PS2 and XBOX.

## CREDITS

- HeitorSpectre  
- Evil Trainer  
- marcos_haf — HNNEWGAMES  

---

## IMAGES
<img width="1184" height="792" alt="image" src="https://github.com/user-attachments/assets/57d010ea-236a-482a-a877-9705b9506b87" />
<img width="1184" height="789" alt="image" src="https://github.com/user-attachments/assets/a4a89905-65fd-46cc-8ecf-961182d2247b" />



## SUPPORTED FORMATS / PLATFORMS

### Containers
- `.hvt` — Nintendo Wii / GameCube standalone texture
- `.hvt` —  Final Exam (HydraVision modern) standalone texture
- `.hvi` — PlayStation 2 standalone paletted texture  
- `.dic` — Texture dictionary (PC, PS2 RenderWare, Wii GX)  
- `.dip` — PC ObsCure 1 texture dictionary (HydraVision)
- `.xbr` —  Xbox classic texture dictionary (NV2A swizzled) 

### PC pixel formats (.dic)
- R8G8B8A8 (32 bpp)  
- R5G6B5 (16 bpp)  
- R5G5B5A1 (16 bpp)  

### PC pixel formats (.dip)
- B8G8R8A8 (32 bpp)  
- B5G6R5 (16 bpp)  
- B5G5R5A1 (16 bpp)  

### PS2 pixel formats
- PAL8 (8 bpp swizzled, RGB5551 or RGBA8888 palette)  
- RGB5551 (16 bpp, little-endian)  
- RGBA8888 (32 bpp, PS2 alpha 0..128)  

### Nintendo Wii / GameCube pixel formats
- I8 (8 bpp grayscale, GX 8x4 tiles)  
- IA8 (16 bpp grayscale + alpha, 4x4 tiles)  
- RGB5A3 (16 bpp, 4x4 tiles)  
- RGBA8 (32 bpp, 4x4 AR/GB interleaved)  
- C4 / C8 (4/8 bpp paletted with TLUT)  
- CMPR (4 bpp DXT1-style, PCA-quantized encoder)

###   Xbox classic pixel formats (.xbr, NV2A swizzled)
- SZ_R5G6B5    (16 bpp, Morton-order swizzle)
- SZ_A1R5G5B5  (16 bpp, Morton-order swizzle)
- SZ_A8R8G8B8  (32 bpp, Morton-order swizzle)

###   Final Exam pixel formats (.hvt, magic "HVI ")
- BGRA      (32 bpp linear)
- BGRX      (32 bpp linear, alpha forced opaque)
- TXD1      (DXT1 / BC1, 4 bpp)
- TXD3      (DXT3 / BC2, 8 bpp)
- TXD5      (DXT5 / BC3, 8 bpp)  
