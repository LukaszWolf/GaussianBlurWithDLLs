option casemap:none
.code

; void MyProc1(void* imagePtr, int width, int height, int stride)
; RCX = imagePtr, EDX = width, R8D = height, R9D = stride (bytes/row)
MyProc1 PROC imagePtr:QWORD, imgWidth:DWORD, imgHeight:DWORD, imgStride:DWORD

    push rbx
    push rsi
    push rdi

    mov     rsi, rcx        ; base ptr
    mov     ebx, edx        ; width
    mov     edi, r8d        ; height
    mov     r10d, r9d       ; stride

    ; y = height / 2  -> start of middle row
    mov     eax, edi
    shr     eax, 1
    imul    eax, r10d
    add     rsi, rax

    ; BGRA = AA 00 AA FF (fully opaque) -> dword LE = 0xFFAA00AA
    mov     eax, 0FFAA00AAh

row_loop:
    test    ebx, ebx
    jz      row_done

    mov     dword ptr [rsi], eax    ; write pixel
    add     rsi, 4                  ; next pixel
    dec     ebx
    jmp     row_loop

row_done:
    pop     rdi
    pop     rsi
    pop     rbx
    ret

MyProc1 ENDP
END
