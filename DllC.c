// DllC.c
#include <windows.h>

__declspec(dllexport) int MyProc2(int a, int b)
{
    return a + b;
}