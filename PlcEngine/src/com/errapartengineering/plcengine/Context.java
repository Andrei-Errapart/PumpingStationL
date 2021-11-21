package com.errapartengineering.plcengine;

import javax.xml.parsers.SAXParser;
import javax.xml.parsers.SAXParserFactory;
import org.xml.sax.Attributes;
import org.xml.sax.SAXException;
import org.xml.sax.helpers.DefaultHandler;
import java.util.*;
import java.io.*;
import android.util.*; // Log.d



/** Configuration of the PLC.
 * 
 * TODO: better name is PlcContext.
 * @author Andrei
 */
public class Context extends DefaultHandler {
	public final String plcDeviceId;
	public final File configurationFile;
	public final File programFile;
    /** TODO: handle versioning correctly. */
    public final int version;
    public final String modbus;
    public final String server;
    
    /** List of signals present in the configuration file. */
    public final List<IOSignal> Signals = new ArrayList<IOSignal>();
    /** List of devices present in the configuration file. */
    public final List<IODevice> Devices = new ArrayList<IODevice>();
    /** Signals arranged by Id. */
    public final Map<Integer, IOSignal> SignalTable = new HashMap<Integer, IOSignal>();
    /** Signals arranged by Name. */
    public final Map<String, IOSignal> SignalMap = new HashMap<String, IOSignal>();
    /** Local signals specified in the logic program. */
    public final Map<String, IOSignal> LocalSignalMap = new HashMap<String, IOSignal>();
    public final List<ComputedSignal> ComputedSignals = new ArrayList<ComputedSignal>();
    /** Logic program, to be executed after each device list scan. */
    public final List<LogicStatement> LogicProgram = new ArrayList<LogicStatement>();

    /** Those queries from the client that are to be executed in the main thread.
     * Synchronize on the collection before operations!
     */
    public final Queue<PlcCommunication.MessageToPlc> Queries = new LinkedList<PlcCommunication.MessageToPlc>();
    
    /** Get the IOSignal from SignalMap or SignalTable either by name or by signal id.
     * 
     * @param NameOrId
     * @return 
     */
    public IOSignal SignalByNameOrId(String NameOrId)
    {
        IOSignal ios = SignalMap.get(NameOrId);
        if (ios==null)
        {
            try {
                int signal_id = Integer.parseInt(NameOrId);
                ios = SignalTable.get(signal_id);
            } catch (Exception ex)
            {
                // pass.
            }
        }
        return ios;
    }
    
    public Context(String plcDeviceId, java.io.File configurationFile, java.io.File programFile, java.io.File  versionFile, String server, String modbus) throws Exception
    {
    	String versionString = FileUtils.ReadFileAsString(versionFile);
    	int version = Integer.parseInt(versionString);
    	this.plcDeviceId = plcDeviceId;
    	this.configurationFile = configurationFile;
    	this.programFile = programFile;
    	this.version = version;
    	this.server = server;
    	this.modbus = modbus;
    	
        // 1. Read the file.
		SAXParserFactory factory = SAXParserFactory.newInstance();
		SAXParser saxParser = factory.newSAXParser();
		
        // Simply using saxParser.parse(Filename) will lock the file by not closing the file when done reading...
        // This method doesn't have this problem.
		java.io.FileInputStream configurationStream = new java.io.FileInputStream(configurationFile);
		try {
	        saxParser.parse(configurationStream, this);
		} finally {
			configurationStream.close();
		}
       
        // 3. Parse the program.
		java.io.FileInputStream programStream = new java.io.FileInputStream(programFile);
		try {
	        Scanner scanner = new Scanner(programStream);
	        Parser parser = new Parser(scanner);
	        parser.Result = this.LogicProgram;
	        parser.Context = this; // we can accept leaking this.
	        parser.Parse();
		} finally {
			programStream.close();
		}
        
        Log.d("Logic program", "" + LogicProgram.size() + " statements, " + LocalSignalMap.size() + " variables.");
    }

    String _device_noreadmultiple = "false";
    int _device_address = 0;
    long _device_readingperiod_ms = 0L;
    List<IOSignal> _device_signals = new ArrayList<IOSignal>();

    private final static String[] _known_tags = new String[] 
	{
    	"plcmaster",
    	"signals",
    	"devices",
    	"variables", 
    	"computedsignals", 
    	"schemes",
    	"scheme",
    	"group",
    	"program",
    	"usesignal"
    };

    @Override
    public void startElement(String uri, String localName,String qName, 
            Attributes atts) throws SAXException
    {
    	String ename = localName.length()>0 ? localName : qName;
    	if (ename.equalsIgnoreCase("device"))
    	{
    		// stuff it away for later use.
    		_device_noreadmultiple = atts.getValue("noreadmultiple");
    		_device_address = _FetchIntegerAttribute(atts, "device", "address");
    		_device_readingperiod_ms = 0L;
    		String s = atts.getValue("readingperiod");
    		if (s!=null && s.length()>0)
    		{
    			_device_readingperiod_ms = (long)(Float.parseFloat(s) * 1000L);
    		}
    		_device_signals.clear();
    	} else if (ename.equalsIgnoreCase("signal"))
        {
            IOSignal ios; // = new IOSignal();
            int ios_id = _FetchIntegerAttribute(atts, ename, "id");
            int ios_IOIndex = _FetchIntegerAttribute(atts, ename, "ioindex");
            String ios_type_name = atts.getValue("type");
            IOType ios_type = IOType.OfString(ios_type_name);
            String ios_name = atts.getValue("name");
            String ios_description = atts.getValue("description");
            boolean ios_skipwritecheck = _FetchBooleanAttribute(atts, ename, "skipwritecheck", false);
            if (ios_name == null)
            {
                ios_name = "";
            }
            if (ios_type == null)
            {
                throw new SAXException("PlcContext.startElement: Invalid value for signal type: '" + ios_type_name+"'");
            }
            else
            {
                // Have to check the startupgroup, too.
                ios = new IOSignal(ios_name, ios_id, ios_type, ios_IOIndex, ios_description==null ? "" : ios_description, ios_skipwritecheck, false);
            }
            Signals.add(ios);
            _CheckDuplicateId(ios);
            SignalTable.put(ios_id, ios);            
            if (ios_name.length()>0)
            {
                _CheckDuplicateName(ios);
                SignalMap.put(ios_name, ios);
            }
            _device_signals.add(ios);
        } else if (ename.equalsIgnoreCase("variable"))
        {
            IOSignal ios; // = new IOSignal();
            String ios_type_name = atts.getValue("type");
            IOType ios_type = IOType.OfString(ios_type_name);
            String ios_name = atts.getValue("name");
            String ios_value = atts.getValue("value");
            boolean ios_skipwritecheck = _FetchBooleanAttribute(atts, ename, "skipwritecheck", false);
            boolean ios_isnonvolatile = _FetchBooleanAttribute(atts, ename, "nonvolatile", false);
            if (ios_name == null || ios_name.length()==0)
            {
                throw new SAXException("PlcContext.startElement: Variable name missing!");
            }
            if (ios_type == null)
            {
                throw new SAXException("PlcContext.startElement: Invalid value for variable type: '" + ios_type_name+"'");
            }
            else
            {
                ios = new IOSignal(ios_name, -1, ios_type, -1, "", ios_skipwritecheck, ios_isnonvolatile);
            }
            if (ios_value!=null)
            {
                int value = Integer.parseInt(ios_value);
                ios.setValue(value);
            }
            // Variables go only to SignalMap and list.
            Signals.add(ios);
            _CheckDuplicateName(ios);
            SignalMap.put(ios_name, ios);
        } else if (ename.equalsIgnoreCase("computedsignal"))
        {
        	// FIXME: implement computed signals!
            String cs_name = atts.getValue("name");
            String cs_type_name = atts.getValue("type");
            String[] cs_source_signal_names = atts.getValue("sources").split(";");
            String cs_parameters = atts.getValue("params");
            String cs_format_string = atts.getValue("formatstring");
            String cs_unit = atts.getValue("unit");
            String cs_description = atts.getValue("description");
            boolean ios_skipwritecheck = _FetchBooleanAttribute(atts, ename, "skipwritecheck", false);
        	ComputedSignal cs = new ComputedSignal(cs_name, cs_type_name, cs_source_signal_names, cs_parameters, cs_format_string, cs_unit, cs_description, ios_skipwritecheck, SignalMap);
        	
        	this.ComputedSignals.add(cs);
        	this.SignalMap.put(cs_name, cs);
        	// this.Signals.add(cs);
        } else {
        	boolean is_known = false;
        	for (String s : _known_tags) {
        		if (ename.equalsIgnoreCase(s)) {
        			is_known = true;
        			break;
        		}
        	}
        	if (!is_known) {
        		throw new SAXException("PlcContext.startElement: Unknown tag: '" + ename + "'.");
        	}
        }
    }

    private void _add_devices(List<IOSignal> src_signals, int start_index, int end_index, IOType type)
    {
    	// 1. start_ioindex, end_ioindex
		int start_ioindex = -1;
		int end_ioindex = -1;
    	for (int signal_index=start_index; signal_index<end_index; ++signal_index)
    	{
			IOSignal s = src_signals.get(signal_index);
			if (s.Type==type)
			{
    			if (start_ioindex<0)
				{
    				start_ioindex = s.IOIndex;
    				end_ioindex = s.IOIndex;
				}
    			else
				{
        			if (start_ioindex>s.IOIndex)
        			{
        				start_ioindex = s.IOIndex;
        			}
        			if (end_ioindex<s.IOIndex)
        			{
        				end_ioindex = s.IOIndex;
        			}
				}
			}
    	}
    	
    	if (start_ioindex>=0)
    	{
			// New device!
			IODevice dev = new IODevice(
					_device_address, type,
					start_ioindex,
					end_ioindex - start_ioindex + 1,
					_device_readingperiod_ms,
					_device_noreadmultiple!=null && _device_noreadmultiple.equalsIgnoreCase("true"));
			Devices.add(dev);
	    	for (int signal_index=start_index; signal_index<end_index; ++signal_index)
	    	{
				IOSignal s = src_signals.get(signal_index);
				if (s.Type==type)
				{
					s.Device = dev;
				}
	    	}
    	}
    }
    
    @Override
    public void endElement (String uri, String localName, String qName) throws SAXException
    {
    	String ename = localName.length()>0 ? localName : qName;
    	if (ename.equalsIgnoreCase("device"))
    	{
    		// yes guys!
    		for (IOType type: new IOType[] { IOType.DISCRETE_INPUT, IOType.COIL, IOType.INPUT_REGISTER, IOType.HOLDING_REGISTER, IOType.REGISTER32 /* no device has REGISTER32, though */ } )
    		{
    			// 1. Collect signals of type 'type' to 't_signals'.
    			int signal_start_index = 0;
        		IOSignal signal_prev = null;
        		
        		// for (IOSignal s : _device_signals)
        		for (int signal_index=0; signal_index<_device_signals.size(); ++signal_index)
        		{
        			IOSignal s = _device_signals.get(signal_index);
        			if (s.Type==type)
        			{
            			// Shall we add it to the devices?
            			if (signal_prev!=null && signal_prev.IOIndex+1 != s.IOIndex)
            			{
            				_add_devices(_device_signals, signal_start_index, signal_index, type);
            				signal_start_index = signal_index;
            			}
            			signal_prev = s;
        			}
        		}
        		
    			_add_devices(_device_signals, signal_start_index, _device_signals.size(), type);
    		}
    	}
    }

    void _CheckDuplicateId(IOSignal ios1) throws SAXException
    {
        IOSignal ios2 = SignalTable.get(ios1.Id);
        if (ios2!=null)
        {
            throw new SAXException("Duplicate id in configuration file: " + ios1.Id);
        }
    }
    
    void _CheckDuplicateName(IOSignal ios1) throws SAXException
    {
        if (ios1.Name!=null && ios1.Name.length()>0)
        {
            IOSignal ios2 = SignalMap.get(ios1.Name);
            if (ios2!=null)
            {
                throw new SAXException("Duplicate name in configuration file: " + ios1.Name);
            }
        }
    }
    
    @Override
    public void characters (char ch[], int start, int length) throws SAXException
    {
    }

    @Override
    public void ignorableWhitespace (char ch[], int start, int length) throws SAXException
    {
    }

    static final int _FetchIntegerAttribute(Attributes atts, String element_name, String attr_name) throws SAXException
    {
        // 1. Get the attribute.
        String s = atts.getValue(attr_name);
        if (s == null)
        {
            throw new SAXException("PlcContext.startElement: Expected tag " + element_name + "." + attr_name+ " to be present, but it is missing.");
        }
        // 2. Parse the string as integer.
        int r = 0;
        try
        {
            r = Integer.parseInt(s);
        }
        catch (Exception ex)
        {
            throw new SAXException("PlcContext.startElement: Expected tag " + element_name + "." + attr_name+ " to have valid integer value, but got '" + s + "'.");
        }
        return r;
    }
    
    static final boolean _FetchBooleanAttribute(Attributes atts, String element_name, String attr_name, boolean defaultValue)
    {
        // 1. Get the attribute.
        String s = atts.getValue(attr_name);
        if (s == null)
        {
        	return defaultValue;
        }
        // 2. Parse the string as integer.
        return s.equalsIgnoreCase("true") || s.equalsIgnoreCase("1");
    }
}
