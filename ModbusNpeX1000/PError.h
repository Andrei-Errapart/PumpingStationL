#ifndef PError_h_
#define PError_h_

#include <exception>	// std::exception
#include <string>	// std::string

// a'la perror.
class PError : public std::exception {
public:
	PError(const char* msg);
	virtual ~PError() throw();
	virtual const char*	what() const throw();
private:
	std::string	_msg;
}; // class PError

#endif /* PError_h_ */

