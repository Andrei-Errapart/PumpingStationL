// C++ headers.
#include <stdexcept>	// std::runtime_error
#include <iostream>
#include <fstream>	// easy (but somewhat tedious) file io.

// C headers.
#include <sys/types.h>
#include <sys/stat.h>
#include <fcntl.h>	// open flags, etc.
#include <unistd.h>	// close, lseek
#include <stdarg.h>	// variable arguments
#include <stdio.h>	// sprintf
#include <stdlib.h>	// atoi
#include <string.h>	// strlen

// Ourselves.
#include <Led.h>	// ourselves.

#ifndef INVALID_HANDLE_VALUE
#define	INVALID_HANDLE_VALUE	(-1)
#endif /* INVALID_HANDLE_VALUE */

#ifndef LED_DIR
#define	LED_DIR	"/sys/devices/platform/leds-gpio.0/leds"
#endif

// TODO: use sprintf operating on std::string buffers.
#define _THROW(args_with_xbuf)          do { char xbuf[200]; sprintf args_with_xbuf; throw std::runtime_error(xbuf); } while(0)


// -----------------------------------------------------------------------------------
Led::Led(const unsigned int index)
:	_index(index)
	,_fd(INVALID_HANDLE_VALUE)
{
	// 3. _fd = GPIO: value.
	char	xbuf[1024];
	sprintf(xbuf, LED_DIR"/LED%u/brightness", index);
	_fd_filename = xbuf;
	_fd = open(_fd_filename.c_str(), O_RDWR);
	if (_fd<0)
	{
		_THROW((xbuf, "Cannot open LED file '%s'!", _fd_filename.c_str()));
	}
}

// -----------------------------------------------------------------------------------
Led::~Led()
{
	if (_fd != INVALID_HANDLE_VALUE)
	{
		close(_fd);
		_fd = INVALID_HANDLE_VALUE;
	}
}

// -----------------------------------------------------------------------------------
void
Led::set(const int val)
{
	_seek0();

	char		fbuf[40];
	sprintf(fbuf, "%d\n", val);
	const int	fbuf_len = strlen(fbuf);

	const int	r_write = write(_fd, fbuf, fbuf_len);
	if (r_write<fbuf_len) {
		_THROW((xbuf, "Led: Unable to write IO %d, because it is input only.", _index));
	}
}

// -----------------------------------------------------------------------------------
void
Led::_seek0()
{
	const off_t	r_seek = lseek(_fd, SEEK_SET, 0);
	if (r_seek==(off_t)-1) {
		_THROW((xbuf, "Failed to seek to the beginning of the file '%s'.", _fd_filename.c_str()));
	}
}

