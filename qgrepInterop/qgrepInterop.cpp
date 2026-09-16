#include <vector>
#include <string>
#include <string.h>

#include <msclr\marshal.h>
#include <msclr\marshal_cppstd.h>

#include <qgrep.hpp>

#include "qgrepInterop.h"

// qgrep 引擎全程以 UTF-8 字节工作（文件内容、查询串、配置路径、搜索结果的路径与行文本）。
// 这里统一用 ISO-8859-1/28591 做「字节 <-> 托管字符串」的无损搬运：每个字节固定映射到
// U+0000..U+00FF，反向也一一对应。绝不能走系统 ANSI 代码页（中文系统为 CP936，会把 UTF-8
// 的 3 字节汉字当双字节字符解码成乱码，也会让 0xB0 这类单字节分隔符被吃掉）。
// 托管侧对应的转换是 ConfigParser.ToUtf8/FromUtf8（同一编码约定）。
namespace
{
    std::string ToNativeBytes(System::String^ value)
    {
        std::string result;

        if (value == nullptr)
            return result;

        result.reserve(value->Length);

        for (int i = 0; i < value->Length; ++i)
        {
            wchar_t ch = value[i];
            result.push_back(ch <= 0xFF ? static_cast<char>(ch) : '?');
        }

        return result;
    }

    System::String^ FromNativeBytes(const char* data)
    {
        if (data == nullptr)
            return System::String::Empty;

        size_t length = strlen(data);
        array<wchar_t>^ chars = gcnew array<wchar_t>(static_cast<int>(length));

        for (size_t i = 0; i < length; ++i)
        {
            chars[static_cast<int>(i)] = static_cast<wchar_t>(static_cast<unsigned char>(data[i]));
        }

        return gcnew System::String(chars);
    }
}

void qgrepInterop::QGrepWrapper::CallQGrepAsync(System::Collections::Generic::List<System::String^>^ arguments, StringCallback^ stringCb, CheckForceStoppedCallback^ checkStoppedCb, ProgressCalback^ progressCb, LocalizedStringCallback^ localizedStringCb)
{
    std::string unmanagedArguments;

    for each (System::String^ argument in arguments)
    {
        unmanagedArguments += ToNativeBytes(argument) + "\n";
    }

    stringCallback = stringCb;
    checkStoppedCalback = checkStoppedCb;
    progressCalback = progressCb;
    localizedStringCalback = localizedStringCb;

    qgrepWrapperAsync(const_cast<char*>(unmanagedArguments.c_str()), (int)unmanagedArguments.size(), &NativeQGrepWrapper::nativeStringCallback, &NativeQGrepWrapper::nativeCheckForceStopped, &NativeQGrepWrapper::nativeProgressCallback, &NativeQGrepWrapper::nativeLocalizedStringCallback);
}

void qgrepInterop::NativeQGrepWrapper::nativeStringCallback(const char* result)
{
    if (QGrepWrapper::stringCallback != nullptr)
    {
        QGrepWrapper::stringCallback(FromNativeBytes(result));
    }
}

void qgrepInterop::NativeQGrepWrapper::nativeProgressCallback(double percentage)
{
    if (QGrepWrapper::progressCalback != nullptr)
    {
        return QGrepWrapper::progressCalback(percentage);
    }
}

bool qgrepInterop::NativeQGrepWrapper::nativeCheckForceStopped()
{
    if (QGrepWrapper::checkStoppedCalback != nullptr)
    {
        return QGrepWrapper::checkStoppedCalback();
    }

    return false;
}

void qgrepInterop::NativeQGrepWrapper::nativeLocalizedStringCallback(const char** result, int size)
{
    if (QGrepWrapper::localizedStringCalback != nullptr)
    {
        List<String^>^ stringsList = gcnew List<String^>();
        for (int i = 0; i < size; i++)
        {
            stringsList->Add(FromNativeBytes(result[i]));
        }

        QGrepWrapper::localizedStringCalback(stringsList);
    }
}