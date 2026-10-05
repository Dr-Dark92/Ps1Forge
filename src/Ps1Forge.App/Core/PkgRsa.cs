using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace Ps1Forge.Core;

public static class PkgRsa
{
    public static byte[] EncryptKey(byte[] modulus, byte[] key)
    {
        if(modulus.Length!=256) throw new ArgumentException("RSA modulus must be 256 bytes.",nameof(modulus));
        if(key.Length!=32) throw new ArgumentException("Wrapped key must be 32 bytes.",nameof(key));

        var seedInput=new byte[288];
        modulus.CopyTo(seedInput,0);
        key.CopyTo(seedInput,256);
        var seedsHash=SHA256.HashData(SHA256.HashData(seedInput));
        var seeds=new uint[8];
        for(var i=0;i<8;i++) seeds[i]=BinaryPrimitives.ReadUInt32BigEndian(seedsHash.AsSpan(i*4,4));
        var mt=new Mt19937(seeds);

        var padded=new byte[256];
        padded[1]=2;
        padded[223]=0;
        key.CopyTo(padded,224);
        Span<byte> source=stackalloc byte[48];
        for(var k=2;k<223;)
        {
            for(var i=0;i<12;i++) BinaryPrimitives.WriteUInt32BigEndian(source.Slice(i*4,4),mt.Next());
            var random=SHA256.HashData(source);
            foreach(var b in random)
            {
                if(k>=223) break;
                if(b!=0) padded[k++]=b;
            }
        }

        var value=new BigInteger(padded,isUnsigned:true,isBigEndian:true);
        var mod=new BigInteger(modulus,isUnsigned:true,isBigEndian:true);
        var result=BigInteger.ModPow(value,65537,mod).ToByteArray(isUnsigned:true,isBigEndian:true);
        if(result.Length>256) result=result[^256..];
        var output=new byte[256];
        result.CopyTo(output,256-result.Length);
        return output;
    }

    private sealed class Mt19937
    {
        private const int N=624,M=397;
        private const uint MatrixA=0x9908B0DF,UpperMask=0x80000000,LowerMask=0x7FFFFFFF;
        private readonly uint[] mt=new uint[N];
        private int mti=N;

        public Mt19937(uint[] seeds)
        {
            if(seeds.Length==0) throw new ArgumentException("MT seed array cannot be empty.",nameof(seeds));
            mt[0]=0x012BD6AA;
            for(var i=1;i<N;i++) mt[i]=unchecked((uint)i+0x6C078965u*(mt[i-1]^(mt[i-1]>>30)));
            var stateIdx=1; var seedIdx=0;
            for(var length=Math.Max(N,seeds.Length);length>0;length--)
            {
                mt[stateIdx]=unchecked((mt[stateIdx]^((mt[stateIdx-1]^(mt[stateIdx-1]>>30))*0x0019660Du))+seeds[seedIdx]+(uint)seedIdx);
                stateIdx++; seedIdx++;
                if(stateIdx>=N){mt[0]=mt[N-1];stateIdx=1;}
                if(seedIdx>=seeds.Length)seedIdx=0;
            }
            for(var length=0;length<N-1;length++)
            {
                mt[stateIdx]=unchecked((mt[stateIdx]^((mt[stateIdx-1]^(mt[stateIdx-1]>>30))*0x5D588B65u))-(uint)stateIdx);
                stateIdx++;
                if(stateIdx>=N){mt[0]=mt[N-1];stateIdx=1;}
            }
            mt[0]=1u<<31;
        }

        public uint Next()
        {
            if(mti>=N)
            {
                int kk;
                for(kk=0;kk<N-M;kk++){var y=(mt[kk]&UpperMask)|(mt[kk+1]&LowerMask);mt[kk]=mt[kk+M]^(y>>1)^((y&1)!=0?MatrixA:0);}
                for(;kk<N-1;kk++){var y=(mt[kk]&UpperMask)|(mt[kk+1]&LowerMask);mt[kk]=mt[kk+M-N]^(y>>1)^((y&1)!=0?MatrixA:0);}
                {var y=(mt[N-1]&UpperMask)|(mt[0]&LowerMask);mt[N-1]=mt[M-1]^(y>>1)^((y&1)!=0?MatrixA:0);}
                mti=0;
            }
            var y=mt[mti++];
            y^=(y>>11)&0x001FFFFF;
            y^=(y<<7)&0x9D2C5680;
            y^=(y<<15)&0xEFC60000;
            y^=(y>>18)&0x00003FFF;
            return y;
        }
    }
}
