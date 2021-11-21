#ifndef Led_h_
#define Led_h_

#include <string>	// std::string

class Led {
public:
	/// Constructor. For the NPE-X1000, index must be in the range of 1..3.
	/// 1 = LED2
	/// 2 = LED1
	/// 3 = LED STATUS
	Led(const unsigned int index);
	~Led();

	/// Set the value. 0=off, any other value=on.
	void set(const int val);
private:
	/// Seek to the beginning of the value file.
	void _seek0();
private:
	const unsigned int	_index;
	int			_fd;
	std::string		_fd_filename;
}; // class Led

#endif /* Led_h_ */

