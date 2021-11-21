#ifndef DigitalIo_h_
#define DigitalIo_h_

#include <string>	// std::string

class DigitalIo {
public:
	typedef enum {
		DIR_IN,
		DIR_OUT
	} DIR;

	/// Constructor. For the NPE-X1000, index must be in the range of 0..223.
	DigitalIo(const unsigned int index, const DIR dir, const bool is_inverted);
	~DigitalIo();

	/// Read the value. Works for both input and output IO-s. 0=false, 1=true
	int operator()();

	/// Set the value. Throws exceptions when used on input IO. 0=false, any other value=true
	void set(const int val);

	/// Is the input/output inverted? This is the case for the optoisolated inputs, for example.
	const bool isInverted;
private:
	/// Seek to the beginning of the value file.
	void _seek0();
private:
	const unsigned int	_index;
	const DIR		_dir;
	int			_fd;
	std::string		_fd_filename;
}; // class DigitalIo

#endif /* DigitalIo_h_ */
