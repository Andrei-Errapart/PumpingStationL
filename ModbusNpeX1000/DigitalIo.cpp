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
#include <DigitalIo.h>	// ourselves.

#ifndef INVALID_HANDLE_VALUE
#define	INVALID_HANDLE_VALUE	(-1)
#endif /* INVALIUD_HANDLE_VALUE */

#ifndef GPIO_DIR
#define	GPIO_DIR	"/sys/class/gpio"
#endif

// TODO: use sprintf operating on std::string buffers.
#define _THROW(args_with_xbuf)          do { char xbuf[200]; sprintf args_with_xbuf; throw std::runtime_error(xbuf); } while(0)


// -----------------------------------------------------------------------------------
static std::string
gpio_filename(const char* fmt, ...)
{
	va_list	ap;
	char	sbuf[200];

	va_start(ap, fmt);
	const int	r = vsnprintf(sbuf, sizeof(sbuf), fmt, ap);
	va_end(ap);

	if (r>=0 && r<(int)sizeof(sbuf)) {
		sbuf[r] = 0;
	} else {
		_THROW((xbuf, "whoops!"));
	}

	std::string	s(GPIO_DIR);
	s += sbuf;
	return s;
}

// -----------------------------------------------------------------------------------
static void
write1(
	const std::string&	filename,
	const std::string&	value)
{
	std::ofstream	f;
	f.open(filename.c_str(), std::ios::out);
	f << value << std::flush;
	f.close();
}

// -----------------------------------------------------------------------------------
DigitalIo::DigitalIo(const unsigned int index, const DIR dir, const bool is_inverted)
:	isInverted(is_inverted)
	,_index(index)
	,_dir(dir)
	,_fd(INVALID_HANDLE_VALUE)
{
	// 1. GPIO: export.
	char	xbuf[400];
	sprintf(xbuf, "%u", index);
	write1(gpio_filename("/export"), xbuf);

	// 2. GPIO: direction.
	write1(gpio_filename("/gpio%u/direction", index), dir==DIR_IN ? "in" : "out");

	// 3. _fd = GPIO: value.
	_fd_filename = gpio_filename("/gpio%u/value", index);
	_fd = open(_fd_filename.c_str(), O_RDWR);
	if (_fd<0)
	{
		_THROW((xbuf, "Cannot open IO file '%s'!", _fd_filename.c_str()));
	}
}

// -----------------------------------------------------------------------------------
DigitalIo::~DigitalIo()
{
	if (_fd != INVALID_HANDLE_VALUE)
	{
		close(_fd);
		_fd = INVALID_HANDLE_VALUE;
	}
}

// -----------------------------------------------------------------------------------
int
DigitalIo::operator()()
{
	_seek0();

	// 1. Read.
	char fbuf[12];
	const int	r_read= read(_fd, fbuf, sizeof(fbuf)-1);
	if (r_read<=0) {
		_THROW((xbuf, "Failed to read IO file '%s'.", _fd_filename.c_str()));
	}
	fbuf[r_read] = 0;

	// 2. This is what've got.
	const int	r = atoi(fbuf);
	return (r==0 ? 0 : 1) ^ (isInverted ? 1 : 0);
}

// -----------------------------------------------------------------------------------
void
DigitalIo::set(const int val)
{
	if (_dir == DIR_OUT) {
		_seek0();

		char		fbuf[40];
		if (isInverted) {
			sprintf(fbuf, "%d\n", (val==0 ? 1 : 0));
		} else {
			sprintf(fbuf, "%d\n", val);
		}
		const int	fbuf_len = strlen(fbuf);

		const int	r_write = write(_fd, fbuf, fbuf_len);
		if (r_write<fbuf_len) {
			_THROW((xbuf, "DigitalIo: Unable to write IO %d, because it is input only.", _index));
		}
	} else {
		_THROW((xbuf, "DigitalIo: Unable to write IO %d, because it is input only.", _index));
	}
}

// -----------------------------------------------------------------------------------
void
DigitalIo::_seek0()
{
	const off_t	r_seek = lseek(_fd, SEEK_SET, 0);
	if (r_seek==(off_t)-1) {
		_THROW((xbuf, "Failed to seek to the beginning of the file '%s'.", _fd_filename.c_str()));
	}
}

