cmake_minimum_required(VERSION 3.24)
project(GP200EditorNative LANGUAGES C CXX)
if(NOT WIN32 OR NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "Windows x64 and MSVC are required")
endif()

# Keep NAMCore's translation-unit parser registrations in the executable.
file(GLOB_RECURSE NAM_SOURCES CONFIGURE_DEPENDS "vendor/namcore/NAM/*.cpp")
add_library(ntc_namcore OBJECT ${NAM_SOURCES})
target_compile_features(ntc_namcore PUBLIC cxx_std_20)
target_include_directories(ntc_namcore PUBLIC
  vendor/namcore vendor/namcore/Dependencies/eigen
  vendor/namcore/Dependencies/nlohmann vendor/namcore/Dependencies/AudioDSPTools)
target_compile_definitions(ntc_namcore PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX NAM_ENABLE_A2_FAST NAM_SAMPLE_FLOAT)
target_compile_options(ntc_namcore PRIVATE /W3 /WX- /permissive- /EHsc /utf-8 /fp:precise)

add_library(ntc_r8brain_base OBJECT vendor/r8brain/r8bbase.cpp)
target_include_directories(ntc_r8brain_base PRIVATE vendor/r8brain)
target_compile_options(ntc_r8brain_base PRIVATE /W0 /WX- /permissive- /EHsc /utf-8 /fp:precise)

add_executable(GP200ConversionWorker
  Converter/worker.cpp
  Converter/upstream/src/native_converter.cpp
  Converter/upstream/src/common.cpp
  Converter/upstream/src/stimulus.cpp
  Converter/upstream/src/corrective_ir.cpp
  Converter/upstream/src/clo_refiner.cpp
  vendor/ooura-soxr/fft4g.c
  $<TARGET_OBJECTS:ntc_r8brain_base>)
target_compile_features(GP200ConversionWorker PRIVATE cxx_std_20)
target_include_directories(GP200ConversionWorker PRIVATE Converter/upstream/src vendor/r8brain vendor/ooura-soxr)
target_link_libraries(GP200ConversionWorker PRIVATE ntc_namcore)
target_compile_definitions(GP200ConversionWorker PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX UNICODE _UNICODE NAM_SAMPLE_FLOAT)
target_compile_options(GP200ConversionWorker PRIVATE /O2 /EHsc /utf-8 /fp:precise /WX-)

add_library(r8brain-gp200 SHARED IR/native-ir/LegacyResampler.cpp IR/reference-r8brain-2017/r8bbase.cpp)
target_compile_features(r8brain-gp200 PRIVATE cxx_std_20)
target_compile_definitions(r8brain-gp200 PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(r8brain-gp200 PRIVATE /O2 /EHsc /fp:precise)

foreach(target ntc_namcore ntc_r8brain_base GP200ConversionWorker r8brain-gp200)
  set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endforeach()
