// C++
#include <exception>	// std::exception
#include <stdexcept>	// std::runtime_error
#include <vector>	// std::vector

// C
#include <assert.h>	// 
#include <stdio.h>	// printf
#include <sys/socket.h>	// socket stuff.
#include <netinet/in.h>
#include <stdio.h>
#include <errno.h>	// perror
#include <unistd.h>	// close, fork, usleep, sysconf, lseek
#include <string.h>	// memset
#include <fcntl.h>
#include <time.h>	// time()
#include <sys/mman.h>	// mmap
#include <sys/types.h>
#include <sys/stat.h>
#include <sys/prctl.h>	// prctl
#include <sys/wait.h>	// waitpid

// Ourselves.
#include <DigitalIo.h>
#include <AnalogInput.h>
#include <Led.h>
#include <PError.h>
#include <modbus_slave.h>
#include <modbus_registers.h>

#define	SERVER_PORT		1502
#define	MMAP_FILENAME		"mbproxy.mm"
#define	AIN_SMOOTH_WINDOW	32

#define	NAME_TCPSERVER_PROCESS		"mbproxy-tcp-s"
#define	NAME_TCPCONNECTION_PROCESS	"mbproxy-tcp-c"

static int listenfd = -1;
static int connfd = -1;

// -----------------------------------------------------------------------------------
static std::vector<DigitalIo*>  registers_din;
static std::vector<DigitalIo*>  registers_dout;
static std::vector<AnalogInput*> registers_ain;

static DigitalIo*		register_flipflop;

// -----------------------------------------------------------------------------------
static void
flipflop()
{
	register_flipflop->set(0);
	register_flipflop->set(1);
	register_flipflop->set(0);
}

// -----------------------------------------------------------------------------------
extern "C"
void Uart_PutChar(const uint8_t ch)
{
	write(connfd, &ch, sizeof(ch));
}

// -----------------------------------------------------------------------------------
static void
serve_client()
{
	// 1. Now we are talking, right? :)
	ModbusSlave_Init();

	// 2. Serve forever.
	char	xbuf[30];
	for (;;) {
		memset(xbuf, 0, sizeof(xbuf));
		const int read_r = read(connfd, xbuf, sizeof(xbuf));
		if (read_r>0) {
			for (int i=0; i<read_r; ++i) {
				Uart_CharReceived(xbuf[i]);
			}
		} else {
			printf("Client disconnected!\n");
			return;
		}
	}
}

// -----------------------------------------------------------------------------------
static void
process_tcp_server(void)
{
	try {
		for (;;) {
			struct sockaddr_in	cliaddr = { 0 };
			socklen_t		clilen = sizeof(cliaddr);
			connfd = accept(listenfd, (struct sockaddr *) &cliaddr, &clilen);
			if (connfd<0) {
				perror("Tcp server: accept");
				continue;
			}

			pid_t			childpid = fork();
			if (childpid<0) {
				perror("Tcp server: fork");
				close(connfd);
				continue;
			}
			if (childpid==0) {
				// child!
				close(listenfd);
				const int	prctl_r = prctl(PR_SET_NAME, (unsigned long)NAME_TCPCONNECTION_PROCESS, 0, 0, 0);
				if (prctl_r<0) {
					perror("tcp connection prctl");
				}
				printf("Client connection: Happily serving!\n");
				serve_client();
				close(connfd);
				return;
			} else {
				// Parent!
				printf("Tcp server: New connection, served by mbproxy-tcp-c (pid %d).\n", childpid);
				close(connfd);
			}
		}
	} catch (const std::exception& e) {
		printf("Error in process-tcp-server: %s.\n", e.what());
	}
}


// -----------------------------------------------------------------------------------
// Read analog inputs AIN_SMOOTH_WINDOW times.
static void
scan_registers_ain()
{
	for (int si=0; si<AIN_SMOOTH_WINDOW; ++si)
	{
		for (unsigned int i=0; i<registers_ain.size(); ++i) {
			registers_ain[i]->scan();
		}
		usleep(4 * 1000);
	}
}
// -----------------------------------------------------------------------------------
int
main(
	int	argc,
	char**	argv)
{
	bool	dump_registers = false;
	try {
		for (int i=1; i<argc; ++i) {
			if (strcmp(argv[i], "-d")==0) {
				dump_registers = true;
			}
		}
		// LED-s will show us the way.
		// LED1 blinking: modbus proxy working.
		Led	led1(2);
		led1.set(1);
		// LED2 blinking: action on the modbus.
		Led	led2(1);
		led2.set(1);

		// Configurable IO needs those in order to:
		// a) Activate weak pullups to 3.3V.
		// b) Configure to digital inputs.
		DigitalIo	dir_01_04(109, DigitalIo::DIR_OUT, false);
		dir_01_04.set(0);
		DigitalIo	dir_05_08(108, DigitalIo::DIR_OUT, false);
		dir_05_08.set(0);
		DigitalIo	dir_09_12(107, DigitalIo::DIR_OUT, false);
		dir_09_12.set(0);
		DigitalIo	dir_13_16(106, DigitalIo::DIR_OUT, false);
		dir_13_16.set(0);

		// -----------------------------------------------------------
		// 1= MMAP the MODBUS REGISTERS.
		const unsigned int	page_size = sysconf(_SC_PAGESIZE);
		const unsigned int	mmap_length = ((sizeof(modbus_registers_t) + page_size - 1) / page_size) * page_size;

		// 1.1 Open the file.
		const int		mmap_fd = open(MMAP_FILENAME, O_RDWR | O_CREAT, 0777);
		if (mmap_fd<0) {
			throw PError("open " MMAP_FILENAME);
		}

		// 1.2 Write zeroes to the file.
		{
			std::vector<uint8_t>	zerobuf(mmap_length, 0);
			const int		write_r = write(mmap_fd, &zerobuf[0], mmap_length);
			if (write_r<0) {
				throw PError("write to " MMAP_FILENAME);
			}
			off_t			lseek_r = lseek(mmap_fd, 0, SEEK_SET);
			if (lseek_r == (off_t)-1) {
				throw PError("seek in " MMAP_FILENAME);
			}
		}

		// 1.3 mmap the registers.
		
		modbus_registers = (modbus_registers_t*)mmap(0, mmap_length, PROT_READ|PROT_WRITE, MAP_SHARED, mmap_fd, 0);
		if (modbus_registers == MAP_FAILED) {
			perror("mmap");
			printf("mbproxy: fs doesn't support mmap, continuing with anonymous mapping.\n");
			modbus_registers = (modbus_registers_t*)mmap(0, mmap_length, PROT_READ|PROT_WRITE, MAP_ANON|MAP_SHARED, -1, 0);
			if (modbus_registers == MAP_FAILED) {
				throw PError("mmap");
			} else {
				memset((void*)modbus_registers, 0, mmap_length);
				unlink(MMAP_FILENAME);
			}
		}
		printf("mbproxy: Data file mapped.\n");

		// -----------------------------------------------------------
		// 2= OPEN NPE-X1000 IO FILES.
		// 2.1 flip-flop.
		register_flipflop = new DigitalIo(128, DigitalIo::DIR_OUT, false);

		// 2.2 Optoisolated inputs
		for (int i=0; i<8; ++i) {
			registers_din.push_back(new DigitalIo(56 + i, DigitalIo::DIR_IN, true));
		}
		
		// 2.3 Configurable IO - set to digital inputs.
		registers_din.push_back(new DigitalIo(111, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo(116, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo(110, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo( 83, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo( 94, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo( 95, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo(204, DigitalIo::DIR_IN, false));
		registers_din.push_back(new DigitalIo( 90, DigitalIo::DIR_IN, false));
		for (int i=0; i<8; ++i) {
			registers_din.push_back(new DigitalIo(79 - i, DigitalIo::DIR_IN, false));
		}

		// 2.4 Relay 1, relay 2
		registers_dout.push_back(new DigitalIo(55, DigitalIo::DIR_OUT, false));
		registers_dout.push_back(new DigitalIo(54, DigitalIo::DIR_OUT, false));

		// 2.5 Optoisolated outputs
		for (int i=0; i<6; ++i) {
			registers_dout.push_back(new DigitalIo(48+i, DigitalIo::DIR_OUT, false));
		}

		// 2.7 Analog inputs.
		for (int i=0; i<8; ++i) {
			registers_ain.push_back(new AnalogInput(7-i, AIN_SMOOTH_WINDOW));
		}
		scan_registers_ain();

		// 2.8 Set to current values.
		for (unsigned int i=0; i<registers_din.size(); ++i) {
			modbus_registers->din[i] = (*registers_din[i])();
		}
		for (unsigned int i=0; i<registers_dout.size(); ++i) {
			modbus_registers->dout[i] = (*registers_dout[i])();
		}
		for (unsigned int i=0; i<registers_ain.size(); ++i) {
			modbus_registers->ain[i] = (*registers_ain[i])();
		}

		// -----------------------------------------------------------
		// 3= TCP server.
		listenfd = socket(AF_INET,SOCK_STREAM,0);
		if (listenfd<0) {
			throw PError("socket");
		}

		int optval = 1;
		setsockopt(listenfd, SOL_SOCKET, SO_REUSEADDR, (const void *)&optval , sizeof(optval));

		struct sockaddr_in	servaddr;
		memset(&servaddr, 0, sizeof(servaddr));
		servaddr.sin_family = AF_INET;
		servaddr.sin_addr.s_addr = htonl(INADDR_ANY);
		servaddr.sin_port = htons(SERVER_PORT);
		const int bind_r = bind(listenfd,(struct sockaddr *)&servaddr,sizeof(servaddr));
		if (bind_r<0) {
			throw PError("bind to server port");
		}

		const int listen_r = listen(listenfd,1024);
		if (listen_r<0) {
			throw PError("listen to the server port");
		}

		const pid_t	pid_tcpserver = fork();
		if (pid_tcpserver<0) {
			throw PError("fork");
		}
		if (pid_tcpserver==0) {
			const int	prctl_r = prctl(PR_SET_NAME, (unsigned long)NAME_TCPSERVER_PROCESS, 0, 0, 0);
			if (prctl_r<0) {
				throw PError("prctl");
			}
			process_tcp_server();
			return 0;
		}
		printf("mbproxy-tcp-server (pid %d) up and running.\n", pid_tcpserver);


		printf("mbproxy: Started.\n");
		time_t		last_modbus_time = 0;
		uint32_t	last_modbus_counter = 0;
		for (;;) {
			scan_registers_ain();

			// 1. inputs.
			for (unsigned int i=0; i<registers_din.size(); ++i) {
				DigitalIo& din = *registers_din[i];
				modbus_registers->din[i] = din();
			}

			// 2. outputs.
			for (unsigned int i=0; i<registers_dout.size(); ++i) {
				DigitalIo& dout = *registers_dout[i];
				dout.set(modbus_registers->dout[i] ? 1 : 0);
			}

			// 3. Analog inputs.
			for (unsigned int i=0; i<registers_ain.size(); ++i) {
				AnalogInput& ain = *registers_ain[i];
				modbus_registers->ain[i] = ain();
			}
			flipflop();

			// 4. Indicate something, right? :)
			const time_t	this_time = time(0);
			const int	led1_val = (this_time & 1)==0 ? 0 : 1;
			led1.set(led1_val);

			const uint32_t	activity_counter = modbus_registers->activity_counter;
			if (activity_counter == last_modbus_counter && (this_time - last_modbus_time)>5) {
				led2.set(0);
			} else {
				// Live!
				led2.set(1 - led1_val);
				// Really live?
				if (activity_counter != last_modbus_counter) {
					last_modbus_time = this_time;
					last_modbus_counter = activity_counter;
				}
			}

			if (dump_registers) {
				printf("DI:");
				for (unsigned int i=0; i<8; ++i) {
					printf(" %d", modbus_registers->din[i]);
				}
				printf(" %d", modbus_registers->din[10]);
				printf(" %d", modbus_registers->din[11]);
				printf(" DO:");
				for (unsigned int i=0; i<2; ++i) {
					printf(" %d", modbus_registers->dout[i]);
				}
				printf(" AIN:");
				for (unsigned int i=0; i<1; ++i ) {
					const int	adc = modbus_registers->ain[0];
					const double	depth = (adc - 819)  * 5.0 / 4095;
					printf("%d %5.3f", adc,depth);
				}
				printf("\n");
			}
		}
		return 0;
	} catch (const std::exception& e) {
		printf("Error: %s\n", e.what());
		return 1;
	}
}

