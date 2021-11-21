set CC=arm-linux-androideabi-gcc

%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o socat.o socat.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioinitialize.o xioinitialize.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiohelp.o xiohelp.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioparam.o xioparam.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiodiag.o xiodiag.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioopen.o xioopen.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioopts.o xioopts.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiosignal.o xiosignal.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiosigchld.o xiosigchld.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioread.o xioread.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiowrite.o xiowrite.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiolayer.o xiolayer.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioshutdown.o xioshutdown.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioclose.o xioclose.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xioexit.o xioexit.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-process.o xio-process.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-fd.o xio-fd.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-fdnum.o xio-fdnum.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-stdio.o xio-stdio.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-pipe.o xio-pipe.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-gopen.o xio-gopen.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-creat.o xio-creat.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-file.o xio-file.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-named.o xio-named.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-socket.o xio-socket.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-interface.o xio-interface.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-listen.o xio-listen.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-unix.o xio-unix.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-ip.o xio-ip.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-ip4.o xio-ip4.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-ip6.o xio-ip6.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-ipapp.o xio-ipapp.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-tcp.o xio-tcp.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-sctp.o xio-sctp.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-rawip.o xio-rawip.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-socks.o xio-socks.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-proxy.o xio-proxy.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-udp.o xio-udp.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-progcall.o xio-progcall.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-exec.o xio-exec.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-system.o xio-system.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-termios.o xio-termios.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-readline.o xio-readline.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-pty.o xio-pty.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-openssl.o xio-openssl.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-streams.o xio-streams.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-ascii.o xio-ascii.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xiolockfile.o xiolockfile.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-tcpwrap.o xio-tcpwrap.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-ext2.o xio-ext2.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o xio-tun.o xio-tun.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o error.o error.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o dalan.o dalan.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o procan.o procan.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o procan-cdefs.o procan-cdefs.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o hostan.o hostan.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o fdname.o fdname.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o sysutils.o sysutils.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o utils.o utils.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o nestlex.o nestlex.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o filan.o filan.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o sycls.o sycls.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o sslcls.o sslcls.c
arm-linux-androideabi-ar r libxio.a xioinitialize.o xiohelp.o xioparam.o xiodiag.o xioopen.o xioopts.o xiosignal.o xiosigchld.o xioread.o xiowrite.o xiolayer.o xioshutdown.o xioclose.o xioexit.o xio-process.o xio-fd.o xio-fdnum.o xio-stdio.o xio-pipe.o xio-gopen.o xio-creat.o xio-file.o xio-named.o xio-socket.o xio-interface.o xio-listen.o xio-unix.o xio-ip.o xio-ip4.o xio-ip6.o xio-ipapp.o xio-tcp.o xio-sctp.o xio-rawip.o xio-socks.o xio-proxy.o xio-udp.o xio-rawip.o xio-progcall.o xio-exec.o xio-system.o xio-termios.o xio-readline.o xio-pty.o xio-openssl.o xio-streams.o xio-ascii.o xiolockfile.o xio-tcpwrap.o xio-ext2.o xio-tun.o error.o dalan.o procan.o procan-cdefs.o hostan.o fdname.o sysutils.o utils.o nestlex.o filan.o sycls.o sslcls.o
arm-linux-androideabi-ranlib libxio.a
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.   -o socat socat.o libxio.a
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o procan_main.o procan_main.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.   -o procan procan_main.o procan.o procan-cdefs.o hostan.o error.o sycls.o sysutils.o utils.o
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.  -I.   -c -o filan_main.o filan_main.c
%CC% -O -D_GNU_SOURCE --sysroot=%PLATFORM_DIR% -Wall -Wno-parentheses  -DHAVE_CONFIG_H -I.   -o filan filan_main.o filan.o fdname.o error.o sycls.o sysutils.o utils.o

