import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(path.resolve('.build/icon/package.json'));
const sharp = require('sharp');
const svg = await readFile('Schoology.WinUI/Assets/ClassWire.svg');
const sizes = [256,128,64,48,32,16];
const images = await Promise.all(sizes.map(size => sharp(svg).resize(size,size).png().toBuffer()));
const header = Buffer.alloc(6 + sizes.length * 16);
header.writeUInt16LE(0,0); header.writeUInt16LE(1,2); header.writeUInt16LE(sizes.length,4);
let offset = header.length;
for (let i=0;i<sizes.length;i++) {
 const size=sizes[i], image=images[i], entry=6+i*16;
 header[entry]=size===256?0:size; header[entry+1]=size===256?0:size;
 header[entry+2]=0; header[entry+3]=0; header.writeUInt16LE(1,entry+4); header.writeUInt16LE(32,entry+6);
 header.writeUInt32LE(image.length,entry+8); header.writeUInt32LE(offset,entry+12); offset+=image.length;
}
await mkdir('Schoology.WinUI/Assets',{recursive:true});
await writeFile('Schoology.WinUI/Assets/ClassWire.ico',Buffer.concat([header,...images]));
console.log('Generated ClassWire.ico from the shared SwiftUI/Tauri SVG mark.');
