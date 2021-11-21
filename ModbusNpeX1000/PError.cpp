#include <exception>	// std::exception
#include <string>	// std::string

#include <PError.h>

#include <string.h>	// strlen
#include <errno.h>	// errno

PError::PError(const char* msg)
:	_msg(msg)
{
	const int	last_errno = errno;

	std::string	pstr;
	pstr.resize(1024); // probably not much longer.
	const char*	s = strerror_r(last_errno, (char*)pstr.c_str(), pstr.size()-1);
	
	if (s==0) {
		_msg += ": no error";
	} else {
		_msg += ':';
		_msg += s;
	}
}

PError::~PError() throw()
{
}

const char*
PError::what() const throw()
{
	return _msg.c_str();
}

