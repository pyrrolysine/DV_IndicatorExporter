#!/usr/bin/python3

import socket
import sys

host = sys.argv[1]
port = int(sys.argv[2])

server = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
server.bind((host, port))

print("Bound")

try:
    while True:
        data, addr = server.recvfrom(0x10000)
        print(data.decode('ascii'))
except BaseException as ex:
    print(ex)

try:
    server.close()
except:
    pass

