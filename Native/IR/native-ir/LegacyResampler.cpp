// GP-200 reconstruction wrapper. Upstream headers are preserved unmodified.
#include "../reference-r8brain-2017/CDSPResampler.h"
#include <vector>
#include <algorithm>
#include <cmath>

extern "C" __declspec(dllexport) int __cdecl gp200_resample24(
    const float* source,int count,int sourceRate,float* output,int outputCount)
{
    if(!source||!output||count<1||sourceRate<1000||sourceRate>768000||outputCount<0||outputCount>1024)return -1;
    try {
        std::vector<double> input(count);
        for(int i=0;i<count;i++){if(!std::isfinite(source[i]))return -2;input[i]=source[i];}
        r8b::CDSPResampler24 resampler((double)sourceRate,44100.0,count,2.0);
        int written=0;bool first=true;
        for(int pass=0;written<outputCount&&pass<10000;pass++) {
            if(!first)std::fill(input.begin(),input.end(),0.0);
            first=false;double* converted=nullptr;
            const int available=resampler.process(input.data(),count,converted);
            if(available<0)return -3;
            const int take=(std::min)(available,outputCount-written);
            for(int i=0;i<take;i++)output[written+i]=(float)converted[i];
            written+=take;
        }
        return written==outputCount?written:-4;
    } catch(...) {return -5;}
}
