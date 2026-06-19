// EmuDOS MT-32 shim — a tiny C API over the header-only munt synth (mt32emu.h, LGPL 2.1).
// Built to emudos_mt32.dll and P/Invoked from the managed side. ROMs are passed as byte
// buffers; munt is opened in ACCURATE analog mode (48 kHz stereo, matching the core's audio).

#include <string.h>
#include "mt32emu.h"

using namespace MT32Emu;

namespace
{
    // Minimal SHA1 (makeROMImage identifies a ROM by its SHA1 digest).
    struct Sha1
    {
        size_t count[2];
        unsigned int state[5];
        unsigned char buffer[64];

        Sha1()
        {
            count[0] = count[1] = 0;
            state[0] = 0x67452301; state[1] = 0xEFCDAB89; state[2] = 0x98BADCFE;
            state[3] = 0x10325476; state[4] = 0xC3D2E1F0;
        }

        static void Transform(unsigned int* state, const void* buf)
        {
            unsigned int block[16];
            memcpy(block, buf, 64);
            unsigned int a = state[0], b = state[1], c = state[2], d = state[3], e = state[4];
            #define ROL(value, bits) (((value) << (bits)) | ((value) >> (32 - (bits))))
            #define BLK0(i) (block[i] = (ROL(block[i],24)&0xFF00FF00)|(ROL(block[i],8)&0x00FF00FF))
            #define BLK(i) (block[i&15] = ROL(block[(i+13)&15]^block[(i+8)&15]^block[(i+2)&15]^block[i&15],1))
            #define R0(v,w,x,y,z,i) z+=((w&(x^y))^y)+BLK0(i)+0x5A827999+ROL(v,5);w=ROL(w,30);
            #define R1(v,w,x,y,z,i) z+=((w&(x^y))^y)+BLK(i)+0x5A827999+ROL(v,5);w=ROL(w,30);
            #define R2(v,w,x,y,z,i) z+=(w^x^y)+BLK(i)+0x6ED9EBA1+ROL(v,5);w=ROL(w,30);
            #define R3(v,w,x,y,z,i) z+=(((w|x)&y)|(w&x))+BLK(i)+0x8F1BBCDC+ROL(v,5);w=ROL(w,30);
            #define R4(v,w,x,y,z,i) z+=(w^x^y)+BLK(i)+0xCA62C1D6+ROL(v,5);w=ROL(w,30);
            R0(a,b,c,d,e,0);R0(e,a,b,c,d,1);R0(d,e,a,b,c,2);R0(c,d,e,a,b,3);R0(b,c,d,e,a,4);
            R0(a,b,c,d,e,5);R0(e,a,b,c,d,6);R0(d,e,a,b,c,7);R0(c,d,e,a,b,8);R0(b,c,d,e,a,9);
            R0(a,b,c,d,e,10);R0(e,a,b,c,d,11);R0(d,e,a,b,c,12);R0(c,d,e,a,b,13);R0(b,c,d,e,a,14);
            R0(a,b,c,d,e,15);R1(e,a,b,c,d,16);R1(d,e,a,b,c,17);R1(c,d,e,a,b,18);R1(b,c,d,e,a,19);
            R2(a,b,c,d,e,20);R2(e,a,b,c,d,21);R2(d,e,a,b,c,22);R2(c,d,e,a,b,23);R2(b,c,d,e,a,24);
            R2(a,b,c,d,e,25);R2(e,a,b,c,d,26);R2(d,e,a,b,c,27);R2(c,d,e,a,b,28);R2(b,c,d,e,a,29);
            R2(a,b,c,d,e,30);R2(e,a,b,c,d,31);R2(d,e,a,b,c,32);R2(c,d,e,a,b,33);R2(b,c,d,e,a,34);
            R2(a,b,c,d,e,35);R2(e,a,b,c,d,36);R2(d,e,a,b,c,37);R2(c,d,e,a,b,38);R2(b,c,d,e,a,39);
            R3(a,b,c,d,e,40);R3(e,a,b,c,d,41);R3(d,e,a,b,c,42);R3(c,d,e,a,b,43);R3(b,c,d,e,a,44);
            R3(a,b,c,d,e,45);R3(e,a,b,c,d,46);R3(d,e,a,b,c,47);R3(c,d,e,a,b,48);R3(b,c,d,e,a,49);
            R3(a,b,c,d,e,50);R3(e,a,b,c,d,51);R3(d,e,a,b,c,52);R3(c,d,e,a,b,53);R3(b,c,d,e,a,54);
            R3(a,b,c,d,e,55);R3(e,a,b,c,d,56);R3(d,e,a,b,c,57);R3(c,d,e,a,b,58);R3(b,c,d,e,a,59);
            R4(a,b,c,d,e,60);R4(e,a,b,c,d,61);R4(d,e,a,b,c,62);R4(c,d,e,a,b,63);R4(b,c,d,e,a,64);
            R4(a,b,c,d,e,65);R4(e,a,b,c,d,66);R4(d,e,a,b,c,67);R4(c,d,e,a,b,68);R4(b,c,d,e,a,69);
            R4(a,b,c,d,e,70);R4(e,a,b,c,d,71);R4(d,e,a,b,c,72);R4(c,d,e,a,b,73);R4(b,c,d,e,a,74);
            R4(a,b,c,d,e,75);R4(e,a,b,c,d,76);R4(d,e,a,b,c,77);R4(c,d,e,a,b,78);R4(b,c,d,e,a,79);
            #undef ROL
            #undef BLK0
            #undef BLK
            #undef R0
            #undef R1
            #undef R2
            #undef R3
            #undef R4
            state[0]+=a; state[1]+=b; state[2]+=c; state[3]+=d; state[4]+=e;
        }

        void Process(const unsigned char* data, size_t len)
        {
            size_t i, j = count[0];
            if ((count[0] += (len << 3)) < j) count[1]++;
            count[1] += (len >> 29);
            j = (j >> 3) & 63;
            if ((j + len) > 63)
            {
                memcpy(&buffer[j], data, (i = 64 - j));
                Transform(state, buffer);
                for (; i + 63 < len; i += 64) Transform(state, &data[i]);
                j = 0;
            }
            else i = 0;
            memcpy(&buffer[j], &data[i], len - i);
        }

        void Finalize(char* outHex /* [41] */)
        {
            unsigned char finalcount[8];
            for (unsigned i = 0; i < 8; i++)
                finalcount[i] = (unsigned char)((count[(i >= 4 ? 0 : 1)] >> ((3 - (i & 3)) * 8)) & 255);
            unsigned char c = 0200;
            Process(&c, 1);
            while ((count[0] & 504) != 448) { c = 0000; Process(&c, 1); }
            Process(finalcount, 8);
            for (unsigned k = 0; k < 20; k++)
            {
                unsigned char byte = (unsigned char)((state[k >> 2] >> ((3 - (k & 3)) * 8)) & 255);
                unsigned char nib0 = byte >> 4, nib1 = byte & 15;
                outHex[k*2+0] = (nib0 < 10 ? '0' : ('a' - 10)) + nib0;
                outHex[k*2+1] = (nib1 < 10 ? '0' : ('a' - 10)) + nib1;
            }
            outHex[40] = '\0';
        }
    };

    struct BufFile : public File
    {
        const Bit8u* bytes;
        size_t length;
        SHA1Digest sha1;

        BufFile(const Bit8u* data, size_t len) : bytes(data), length(len)
        {
            Sha1 ctx;
            ctx.Process(data, len);
            ctx.Finalize(sha1);
        }

        size_t getSize() { return length; }
        const Bit8u* getData() { return bytes; }
        const SHA1Digest& getSHA1() { return sha1; }
        void close() {}
    };
}

extern "C"
{
    __declspec(dllexport) void* mt32_create(
        const unsigned char* control, int controlLen, const unsigned char* pcm, int pcmLen)
    {
        BufFile controlFile((const Bit8u*)control, (size_t)controlLen);
        BufFile pcmFile((const Bit8u*)pcm, (size_t)pcmLen);

        const ROMImage* controlImage = ROMImage::makeROMImage(&controlFile);
        const ROMImage* pcmImage = ROMImage::makeROMImage(&pcmFile);

        Synth* synth = new Synth();
        bool ok = controlImage && pcmImage
            && synth->open(*controlImage, *pcmImage, DEFAULT_MAX_PARTIALS, AnalogOutputMode_ACCURATE);

        if (controlImage) ROMImage::freeROMImage(controlImage);
        if (pcmImage) ROMImage::freeROMImage(pcmImage);

        if (!ok || !synth->isOpen()) { delete synth; return nullptr; }
        return synth;
    }

    __declspec(dllexport) int mt32_sample_rate(void* handle)
    {
        return handle ? (int)((Synth*)handle)->getStereoOutputSampleRate() : 0;
    }

    __declspec(dllexport) void mt32_play_msg(void* handle, unsigned int msg)
    {
        if (handle) ((Synth*)handle)->playMsg((Bit32u)msg);
    }

    __declspec(dllexport) void mt32_play_sysex(void* handle, const unsigned char* data, int len)
    {
        if (handle && len > 0) ((Synth*)handle)->playSysex((const Bit8u*)data, (Bit32u)len);
    }

    __declspec(dllexport) void mt32_render(void* handle, short* out, int frames)
    {
        if (handle && frames > 0) ((Synth*)handle)->render((Bit16s*)out, (Bit32u)frames);
    }

    __declspec(dllexport) void mt32_free(void* handle)
    {
        if (handle) { Synth* s = (Synth*)handle; s->close(); delete s; }
    }
}
