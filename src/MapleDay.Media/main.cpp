#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <cstdio>
#include <cstdlib>
#include <fcntl.h>
#include <io.h>
#include <vector>
using Microsoft::WRL::ComPtr;

// In-process codecs supplied by Windows; no installer, external codec or DLL bundle.
static HRESULT Encode(const wchar_t* path, UINT32 width, UINT32 height, UINT32 fps) {
    ComPtr<IMFAttributes> attributes; HRESULT hr = MFCreateAttributes(&attributes, 2);
    if (FAILED(hr)) return hr;
    attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
    attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, TRUE);
    ComPtr<IMFSinkWriter> writer;
    hr = MFCreateSinkWriterFromURL(path, nullptr, attributes.Get(), &writer); if (FAILED(hr)) return hr;
    ComPtr<IMFMediaType> output, input; DWORD stream = 0;
    hr = MFCreateMediaType(&output); if (FAILED(hr)) return hr;
    output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); output->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    output->SetUINT32(MF_MT_AVG_BITRATE, width * height * fps > 100000000 ? 14000000 : 8000000);
    output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    MFSetAttributeSize(output.Get(), MF_MT_FRAME_SIZE, width, height);
    MFSetAttributeRatio(output.Get(), MF_MT_FRAME_RATE, fps, 1);
    MFSetAttributeRatio(output.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    hr = writer->AddStream(output.Get(), &stream); if (FAILED(hr)) return hr;
    hr = MFCreateMediaType(&input); if (FAILED(hr)) return hr;
    input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    input->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    input->SetUINT32(MF_MT_DEFAULT_STRIDE, width * 4);
    MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, width, height);
    MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, fps, 1);
    MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    hr = writer->SetInputMediaType(stream, input.Get(), nullptr); if (FAILED(hr)) return hr;
    hr = writer->BeginWriting(); if (FAILED(hr)) return hr;
    const DWORD length = width * height * 4; std::vector<BYTE> pixels(length); LONGLONG frame = 0;
    while (true) {
        size_t read = 0;
        while (read < length) { auto part = fread(pixels.data() + read, 1, length - read, stdin); if (!part) break; read += part; }
        if (!read) break;
        if (read != length) return E_INVALIDARG;
        ComPtr<IMFMediaBuffer> buffer; hr = MFCreateMemoryBuffer(length, &buffer); if (FAILED(hr)) return hr;
        BYTE* destination = nullptr; hr = buffer->Lock(&destination, nullptr, nullptr); if (FAILED(hr)) return hr;
        // MF_MT_DEFAULT_STRIDE is explicitly positive: keep the renderer's top-down scan lines.
        memcpy(destination, pixels.data(), length);
        buffer->Unlock(); buffer->SetCurrentLength(length);
        ComPtr<IMFSample> sample; hr = MFCreateSample(&sample); if (FAILED(hr)) return hr;
        sample->AddBuffer(buffer.Get()); const auto start = frame * 10000000 / fps;
        sample->SetSampleTime(start); sample->SetSampleDuration((frame + 1) * 10000000 / fps - start);
        hr = writer->WriteSample(stream, sample.Get()); if (FAILED(hr)) return hr; ++frame;
    }
    return frame > 0 ? writer->Finalize() : E_INVALIDARG;
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 5) return 2;
    const auto width = static_cast<UINT32>(_wtoi(argv[2])), height = static_cast<UINT32>(_wtoi(argv[3])), fps = static_cast<UINT32>(_wtoi(argv[4]));
    if (width < 16 || width > 4096 || height < 16 || height > 4096 || fps < 1 || fps > 120 || width % 2 || height % 2) return 2;
    _setmode(_fileno(stdin), _O_BINARY);
    auto hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED); if (FAILED(hr)) return 3;
    hr = MFStartup(MF_VERSION);
    if (SUCCEEDED(hr)) { hr = Encode(argv[1], width, height, fps); MFShutdown(); }
    CoUninitialize();
    if (FAILED(hr)) { fprintf(stderr, "Media Foundation encoding failed: 0x%08lX\n", static_cast<unsigned long>(hr)); return 1; }
    return 0;
}
