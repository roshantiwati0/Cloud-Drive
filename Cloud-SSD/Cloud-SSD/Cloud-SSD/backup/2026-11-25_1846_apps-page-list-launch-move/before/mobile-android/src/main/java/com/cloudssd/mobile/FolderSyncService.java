package com.cloudssd.mobile;

import android.app.job.JobParameters;
import android.app.job.JobService;

import java.util.concurrent.atomic.AtomicBoolean;

/** Runs {@link FolderSync} for every synced folder when Android says it is time (every hour, on Wi-Fi). */
public class FolderSyncService extends JobService {
    private final AtomicBoolean stop = new AtomicBoolean();

    @Override
    public boolean onStartJob(final JobParameters params) {
        stop.set(false);
        new Thread(() -> {
            try {
                FolderSync.runAll(FolderSyncService.this, stop);
            } finally {
                jobFinished(params, false);
            }
        }, "cloudssd-folder-sync").start();
        return true;
    }

    /** Android wants the time back: stop after the current file; the next run carries on. */
    @Override
    public boolean onStopJob(JobParameters params) {
        stop.set(true);
        return false;
    }
}
