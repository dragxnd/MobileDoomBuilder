package org.mobileudb;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.os.Bundle;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.provider.DocumentsContract;
import android.util.AtomicFile;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.FileNotFoundException;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.security.MessageDigest;
import java.util.Locale;

// SAF grants apply only to selected WAD files. Settings live in private app files.
public final class DocumentBridge extends Fragment {
    private static final String TAG="MobileUDBDocuments";
    private static final long WAD_LIMIT=360L*1024*1024, SETTINGS_LIMIT=8L*1024*1024;
    private static final Object WAD_GATE=new Object();
    private String target,filename,exportPath,settingsExport,cacheDirectory;
    private boolean saving,wadFile;
    private long maximumBytes;

    public static void open(Activity activity,String target,String cache,long maximum){launch(activity,target,null,null,null,cache,maximum,false);}
    public static void saveFile(Activity activity,String target,String filename,String path){launch(activity,target,filename,path,null,null,0,false);}
    public static void openWad(Activity activity,String target,String cache,long maximum){launch(activity,target,null,null,null,cache,maximum,true);}
    public static void createWad(Activity activity,String target,String filename,String wadPath,String dbsPath){launch(activity,target,filename,wadPath,dbsPath,null,0,true);}
    private static void launch(Activity activity,String target,String filename,String path,String settings,String cache,long maximum,boolean wadFile) {
        activity.runOnUiThread(()->{
            if(activity.getFragmentManager().findFragmentByTag(TAG)!=null){UnityPlayer.UnitySendMessage(target,"OnNativeError","Диалог выбора файла уже открыт.");return;}
            DocumentBridge fragment=new DocumentBridge();fragment.target=target;fragment.filename=filename;fragment.exportPath=path;fragment.settingsExport=settings;
            fragment.saving=filename!=null;fragment.wadFile=wadFile;fragment.cacheDirectory=cache;fragment.maximumBytes=maximum;
            activity.getFragmentManager().beginTransaction().add(fragment,TAG).commitAllowingStateLoss();
        });
    }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);setRetainInstance(true);
        if(target==null){dismiss();return;}if(state!=null)return;
        Intent intent=new Intent(saving?Intent.ACTION_CREATE_DOCUMENT:Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType(saving?"application/octet-stream":"*/*");
        if(saving)intent.putExtra(Intent.EXTRA_TITLE,filename);
        if(wadFile)intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        try{startActivityForResult(intent,71);}catch(Exception error){send("OnNativeError",error.getMessage());dismiss();}
    }
    @Override public void onActivityResult(int request,int result,Intent data) {
        if(request!=71)return;Activity activity=getActivity();
        if(result!=Activity.RESULT_OK||data==null||data.getData()==null){send("OnNativeError","Выбор файла отменён.");dismiss();return;}
        Uri uri=data.getData();
        if(wadFile){
            persist(activity,uri,data.getFlags());
            if(saving)writeWadDocument(activity,target,uri.toString(),filename,exportPath,settingsExport,true);
            else readWad(activity,target,uri.toString(),cacheDirectory,maximumBytes);
            dismiss();return;
        }
        new Thread(()->{
            File copied=null;boolean handed=false;
            try{
                if(saving){write(activity,uri,exportPath,WAD_LIMIT);send("OnNativeSaved","");}
                else{
                    String name=importName(activity,uri);long limit=Math.min(maximumBytes,WAD_LIMIT);String lower=name.toLowerCase(Locale.ROOT);
                    if(lower.endsWith(".deh")||lower.endsWith(".bex"))limit=Math.min(limit,SETTINGS_LIMIT);
                    copied=copy(activity,uri,cacheDirectory,limit);
                    JSONObject receipt=new JSONObject();receipt.put("name",name);receipt.put("path",copied.getAbsolutePath());send("OnNativeDocument",receipt.toString());handed=true;
                }
            }catch(Exception error){send("OnNativeError","Ошибка файла: "+error.getMessage());}
            finally{if(!handed&&copied!=null)copied.delete();activity.runOnUiThread(this::dismiss);}
        },"MobileUDBFileCopy").start();
    }
    private String importName(Activity activity,Uri uri) {
        try{return displayName(activity,uri);}catch(Exception ignored){String name=uri.getLastPathSegment();return name==null?"Ресурсы":name;}
    }
    private void send(String method,String value){if(target!=null)UnityPlayer.UnitySendMessage(target,method,value==null?"":value);}
    private void dismiss(){if(getActivity()!=null)getActivity().getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();exportPath=null;settingsExport=null;}
    private static void persist(Activity activity,Uri uri,int offered) {
        int flags=offered&(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION);if(flags==0)return;
        try{activity.getContentResolver().takePersistableUriPermission(uri,flags);}catch(Exception ignored){/* Some providers grant access only for this session. */}
    }
    private static String displayName(Activity activity,Uri uri) throws Exception {
        try(Cursor cursor=activity.getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){
            if(cursor!=null&&cursor.moveToFirst()){String name=cursor.getString(0);if(name!=null&&!name.isEmpty())return name;}
        }throw new Exception("Нет имени документа.");
    }
    private static AtomicFile settingsFile(Activity activity,Uri uri) throws Exception {
        String identity;
        // A tree-based legacy URI and a newly selected file URI identify the same document.
        try{identity="document:"+uri.getAuthority()+":"+DocumentsContract.getDocumentId(uri);}catch(Exception ignored){identity="uri:"+uri.toString();}
        byte[] digest=MessageDigest.getInstance("SHA-256").digest(identity.getBytes("UTF-8"));StringBuilder key=new StringBuilder();
        for(byte b:digest)key.append(String.format(Locale.ROOT,"%02x",b&255));
        return new AtomicFile(new File(new File(activity.getFilesDir(),"WadSettings"),key+".dbs"));
    }
    private static byte[] readSettings(AtomicFile file) throws Exception {
        // openRead also recovers an interrupted AtomicFile commit before reading.
        try(InputStream input=file.openRead();ByteArrayOutputStream output=new ByteArrayOutputStream()){
            transfer(input,output,SETTINGS_LIMIT);return output.toByteArray();
        }catch(FileNotFoundException missing){return null;}
    }
    private static void writeSettings(AtomicFile file,String source,byte[] bytes) throws Exception {
        File directory=file.getBaseFile().getParentFile();if(!directory.isDirectory()&&!directory.mkdirs())throw new Exception("Не удалось создать хранилище настроек.");
        FileOutputStream output=null;
        try{
            output=file.startWrite();
            if(source!=null){try(InputStream input=new FileInputStream(source)){transfer(input,output,SETTINGS_LIMIT);}}
            else{if(bytes==null||bytes.length>SETTINGS_LIMIT)throw new Exception("Некорректные настройки WAD.");output.write(bytes);}
            output.getFD().sync();file.finishWrite(output);output=null;
        }finally{if(output!=null)file.failWrite(output);}
    }
    private static void transfer(InputStream input,OutputStream output,long maximum) throws Exception {
        if(input==null||output==null)throw new Exception("Документ недоступен.");byte[] buffer=new byte[65536];int n;long total=0;
        while((n=input.read(buffer))!=-1){total+=n;if(total>maximum)throw new Exception("Документ превышает допустимый размер.");output.write(buffer,0,n);}
    }
    private static File copy(Activity activity,Uri uri,String cache,long maximum) throws Exception {
        File directory=new File(cache);if(!directory.isDirectory()&&!directory.mkdirs())throw new Exception("Не удалось создать кэш.");
        File file=File.createTempFile("MobileUDB-import-",".tmp",directory);boolean success=false;
        try(InputStream input=activity.getContentResolver().openInputStream(uri);OutputStream output=new FileOutputStream(file)){transfer(input,output,maximum);success=true;return file;}
        finally{if(!success)file.delete();}
    }
    public static void readWad(Activity activity,String target,String document,String cache,long maximum) {
        new Thread(()->{
            File wad=null,dbs=null;boolean handed=false;
            try{
                synchronized(WAD_GATE){
                    Uri uri=Uri.parse(document);String name=displayName(activity,uri);if(!name.toLowerCase(Locale.ROOT).endsWith(".wad"))throw new Exception("Выберите файл WAD.");
                    wad=copy(activity,uri,cache,Math.min(maximum,WAD_LIMIT));JSONObject receipt=new JSONObject();receipt.put("name",name);receipt.put("uri",document);receipt.put("path",wad.getAbsolutePath());
                    try{
                        byte[] settings=readSettings(settingsFile(activity,uri));
                        if(settings!=null){dbs=File.createTempFile("MobileUDB-import-",".tmp",new File(cache));try(OutputStream output=new FileOutputStream(dbs)){output.write(settings);}receipt.put("settingsPath",dbs.getAbsolutePath());}
                    }catch(Exception error){if(dbs!=null){dbs.delete();dbs=null;}receipt.put("warning","Настройки WAD недоступны: "+error.getMessage());}
                    UnityPlayer.UnitySendMessage(target,"OnNativeWadRead",receipt.toString());handed=true;
                }
            }catch(Exception error){UnityPlayer.UnitySendMessage(target,"OnNativeError","Ошибка открытия WAD: "+error.getMessage());}
            finally{if(!handed){if(wad!=null)wad.delete();if(dbs!=null)dbs.delete();}}
        },"MobileUDBReadWad").start();
    }
    private static void write(Activity activity,Uri uri,String source,long maximum) throws Exception {
        try(InputStream input=new FileInputStream(source);OutputStream output=activity.getContentResolver().openOutputStream(uri,"wt")){transfer(input,output,maximum);}
    }
    public static void writeWad(Activity activity,String target,String document,String name,String wadPath,String dbsPath){writeWadDocument(activity,target,document,name,wadPath,dbsPath,false);}
    private static void writeWadDocument(Activity activity,String target,String document,String name,String wadPath,String dbsPath,boolean created) {
        new Thread(()->{synchronized(WAD_GATE){
            File oldWad=null;AtomicFile settings=null;byte[] oldSettings=null;Uri uri=null;boolean wadTouched=false,settingsTouched=false;
            try{
                if(name==null||name.indexOf('/')>=0||name.indexOf('\\')>=0||!name.toLowerCase(Locale.ROOT).endsWith(".wad"))throw new Exception("Некорректное имя WAD.");
                uri=Uri.parse(document);String actualName=wadPath==null?name:displayName(activity,uri);
                if(!actualName.toLowerCase(Locale.ROOT).endsWith(".wad"))throw new Exception("Выберите файл WAD.");
                settings=settingsFile(activity,uri);oldSettings=readSettings(settings);
                if(wadPath!=null&&!created)oldWad=copy(activity,uri,new File(dbsPath).getParentFile().getParent(),WAD_LIMIT);
                // Commit private settings first; failed WAD writes restore both original snapshots.
                writeSettings(settings,dbsPath,null);settingsTouched=true;
                if(wadPath!=null){wadTouched=true;write(activity,uri,wadPath,WAD_LIMIT);}
                JSONObject receipt=new JSONObject();receipt.put("uri",document);receipt.put("name",actualName);UnityPlayer.UnitySendMessage(target,"OnNativeWadSaved",receipt.toString());
            }catch(Exception error){String message="Ошибка сохранения WAD: "+error.getMessage();
                try{if(created&&uri!=null)DocumentsContract.deleteDocument(activity.getContentResolver(),uri);else if(wadTouched&&oldWad!=null)write(activity,uri,oldWad.getAbsolutePath(),WAD_LIMIT);}catch(Exception restore){message+=". WAD не восстановлен: "+restore.getMessage();}
                try{if(settingsTouched){if(oldSettings==null)settings.delete();else writeSettings(settings,null,oldSettings);}}catch(Exception restore){message+=". Настройки не восстановлены: "+restore.getMessage();}
                UnityPlayer.UnitySendMessage(target,"OnNativeError",message);
            }finally{if(oldWad!=null)oldWad.delete();}
        }},"MobileUDBWriteWad").start();
    }
}
