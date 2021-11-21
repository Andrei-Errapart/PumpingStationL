#ifndef AnalogInput_h_
#define AnalogInput_h_

#include <string>	// std::string
#include <vector>	// std::vector

/// Analog input for use with MCP3208 in NPE-X1000.
/// 12-bit ADC.
class AnalogInput {
public:
	/// Constructor. For the NPE-X1000, index must be in the range of 0..223.
	AnalogInput(const unsigned int index, const unsigned int n_average);
	~AnalogInput();

	/// Read the value.
	int operator()();

	/// Scan another round.
	void scan();
private:
	const unsigned int	_index;
	int			_fd;
	std::string		_fd_filename;
	/// averaging buffer.
	std::vector<int>	_buffer;
	/// sum of the averaging buffer.
	int			_buffer_sum;
	/// index into _buffer: next value to be written.
	int			_buffer_index;
}; // class AnalogInput

#endif /* AnalogInput_h_ */

