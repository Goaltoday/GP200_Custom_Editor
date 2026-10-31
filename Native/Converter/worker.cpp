#include "native_converter.hpp"
#include "common.hpp"
#include <json.hpp>
#include <fstream>
#include <iostream>
#include <cmath>
#include <windows.h>
using nlohmann::json;
int wmain(int argc,wchar_t** argv) {
 try {
  if(argc!=2){std::cerr<<"Usage: GP200ConversionWorker request.json\n";return 2;}
  std::ifstream file{std::filesystem::path(argv[1])};json request;file>>request;
  const auto path=[](const json& value){return std::filesystem::u8path(value.get<std::string>());};
  ntc::StimulusConfig stimulus;ntc::CorrectiveIrConfig correction;ntc::CloRefineConfig refine;
  if(request.contains("correctiveIr")&&!request["correctiveIr"].is_null()){correction.enabled=true;correction.wav=path(request["correctiveIr"]);}
  if(request.contains("referenceWav")&&!request["referenceWav"].is_null())refine.referenceWav=path(request["referenceWav"]);
  auto result=ntc::convertNamToClo(path(request.at("inputNam")),path(request.at("outputDirectory")),stimulus,correction,refine,{},[](const std::wstring& text){std::cout<<json({{"kind","progress"},{"message",ntc::toUtf8(text)}}).dump()<<std::endl;});
  json output={{"ok",result.ok},{"error",result.error},{"outputClo",ntc::pathToUtf8(result.outputClo)},{"engine","NamToClo 2.10.1"},{"destination","GP200"},{"toneMatch",true},{"analysisFrames",result.toneMatch.analysisFrames}};
  if(std::isfinite(result.toneMatch.finalRmseDb))output["rmseDb"]=result.toneMatch.finalRmseDb;
  auto destination=path(request.at("resultFile"));std::ofstream report(destination);report<<output.dump(2);report.close();
  std::cout<<json({{"kind","result"},{"result",output}}).dump()<<std::endl;return result.ok?0:1;
 }catch(const std::exception& error){std::cerr<<error.what()<<std::endl;return 1;}
}
