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
#include <AnalogInput.h>	// ourselves.

#ifndef INVALID_HANDLE_VALUE
#define	INVALID_HANDLE_VALUE	(-1)
#endif /* INVALIUD_HANDLE_VALUE */

#ifndef ADC_DIR
#define	ADC_DIR	"/sys/devices/platform/spi_gpio.2/spi2.0"
#endif

// TODO: use sprintf operating on std::string buffers.
#define _THROW(args_with_xbuf)          do { char xbuf[200]; sprintf args_with_xbuf; throw std::runtime_error(xbuf); } while(0)


// -----------------------------------------------------------------------------------
static std::string
adc_filename(const unsigned int index)
{
	char	sbuf[200];

	sprintf(sbuf, ADC_DIR"/adc%u_in", index);
	return sbuf;
}

// -----------------------------------------------------------------------------------
AnalogInput::AnalogInput(const unsigned int index, const unsigned int n_average)
:	_index(index)
	,_fd(INVALID_HANDLE_VALUE)
	,_fd_filename(adc_filename(index))
	,_buffer(n_average, 0)
	,_buffer_sum(0)
	,_buffer_index(0)
{
	_fd = open(_fd_filename.c_str(), O_RDONLY);
	if (_fd<0)
	{
		_THROW((xbuf, "Cannot open ADC file '%s'!", _fd_filename.c_str()));
	}
}

// -----------------------------------------------------------------------------------
AnalogInput::~AnalogInput()
{
	if (_fd != INVALID_HANDLE_VALUE)
	{
		close(_fd);
		_fd = INVALID_HANDLE_VALUE;
	}
}

// -----------------------------------------------------------------------------------
int
AnalogInput::operator()()
{
	return _buffer_sum / _buffer.size();
}

// -----------------------------------------------------------------------------------
void
AnalogInput::scan()
{
	// 1. Seek to the beginning.
	const off_t	r_seek = lseek(_fd, SEEK_SET, 0);
	if (r_seek==(off_t)-1) {
		_THROW((xbuf, "AnalogInput: Failed to seek to the beginning of the file '%s'.", _fd_filename.c_str()));
	}

	// 1. Read.
	char fbuf[12];
	const int	r_read= read(_fd, fbuf, sizeof(fbuf)-1);
	if (r_read<=0) {
		_THROW((xbuf, "AnalogInput: Failed to read ADC file '%s'.", _fd_filename.c_str()));
	}
	fbuf[r_read] = 0;

	// 2. This is what've got.
	const int	new_val = atoi(fbuf);
	const int	old_val = _buffer[_buffer_index];
	_buffer[_buffer_index] = new_val;
	_buffer_sum += new_val - old_val;
	_buffer_index = (_buffer_index + 1) % _buffer.size();
}

