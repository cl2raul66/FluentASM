extern _printf
extern _exit

section .data
    mensaje db 'Hola mundo desde Windows', 0

section .text
    global main

main:
    ; printf("Hola mundo desde Windows")
    lea rcx, [rel mensaje]
    call _printf

    ; exit(0)
    xor rdx, rdx
    call _exit
